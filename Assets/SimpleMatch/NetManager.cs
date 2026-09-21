using System;
using System.Collections;
using System.Collections.Generic;
using Mirror;
using PixelArena;
using UnityEngine;
using UnityEngine.SceneManagement;
[RequireComponent(typeof(RoomInterestManagement))]
public class NetManager : NetworkManager
{
    [Header("SimpleMatch")]
    public string arenaScene = "Assets/PixelArena/Scenes/Arena.unity";
    [Min(1)] public int roomCapacity = 5;
    [NonSerialized] public MatchService matchService;
    public MatchManager matchManager;
    public Guid ClientMatchId { get; private set; }
    public MatchPhase ClientPhase { get; private set; }
    public string StatusText { get; private set; } = "Disconnected";
    public event Action<MatchPhase, string> ClientStatusChanged;
    public event Action<KillFeedMessage> KillFeedReceived;
    readonly Queue<Room> loadQueue = new();
    readonly Dictionary<Scene, List<BehaviourState>> presentation = new();
    readonly Dictionary<Scene, List<RendererState>> renderers = new();
    bool loadingRooms;
    int serverGeneration;
    AsyncOperation pendingServerLoad;
    AsyncOperation pendingClientLoad;
    readonly Dictionary<Scene, bool> visibleScenes = new();
    Scene clientArena;
    bool loadingClient;
    int clientGeneration;
    struct BehaviourState { public Behaviour component; public bool enabled; }
    struct RendererState { public Renderer component; public bool enabled; }
    public override void Awake()
    {
        base.Awake();
        autoCreatePlayer = false;
#if UNITY_WEBGL && !UNITY_EDITOR
        // Browsers block ws:// from an https:// page. In production, use the
        // webpage's WSS endpoint and let the hosting proxy forward to port 7777.
        if (Application.absoluteURL.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
            && transport is Mirror.SimpleWeb.SimpleWebTransport webTransport)
        {
            webTransport.clientUseWss = true;
            webTransport.clientWebsocketSettings = new Mirror.SimpleWeb.ClientWebsocketSettings
            {
                ClientPortOption = Mirror.SimpleWeb.WebsocketPortOption.MatchWebpageProtocol,
                CustomClientPort = webTransport.port
            };
        }
#endif
    }
    void Status(MatchPhase phase, string detail)
    { ClientPhase = phase; StatusText = detail; ClientStatusChanged?.Invoke(phase, detail); }
    public override void OnStartServer()
    {
        matchService = new MatchService(this);
        matchService.OnCreateRoom += QueueRoom;
        NetworkServer.RegisterHandler<JoinOrCreateRoomMessage>(matchService.OnJoinCreateRoom);
        NetworkServer.RegisterHandler<LeaveRoomMessage>(matchService.OnServerLeaveRoom);
        NetworkServer.RegisterHandler<ClientReadyMsg>(matchService.OnReadyPlayer);
        StressTestRuntime.ServerStarted();
    }
    public override void OnServerAddPlayer(NetworkConnectionToClient conn) { } // Only validated room readiness creates a player.
    public override void OnServerReady(NetworkConnectionToClient conn) { } // ReadyMessage cannot bypass room loading.
    public override void OnStopServer()
    {
        StressTestRuntime.ServerStopped();
        serverGeneration++; matchService?.Shutdown(); matchService = null; loadQueue.Clear(); loadingRooms = false;
    }
    public override void OnServerDisconnect(NetworkConnectionToClient conn)
    {
        StressTestRuntime.ServerDisconnected(conn);
        matchService?.OnServerLeaveRoom(conn);
        base.OnServerDisconnect(conn);
    }
    void QueueRoom(Guid id)
    {
        loadQueue.Enqueue(matchService.FindRoomById(id));
        if (!loadingRooms) StartCoroutine(LoadRooms());
    }
    IEnumerator LoadRooms()
    {
        loadingRooms = true;
        int generation = serverGeneration;
        while (pendingServerLoad != null && !pendingServerLoad.isDone) yield return null;
        if (generation != serverGeneration) yield break;
        // Defer so the first member is inserted before validation can fail.
        yield return null;
        while (generation == serverGeneration && loadQueue.Count > 0)
        {
            var room = loadQueue.Dequeue();
            if (room.Closing || !NetworkServer.active) continue;
            if (matchManager == null || !Application.CanStreamedLevelBeLoaded(room.sceneName))
            { FailRoom(room, "Arena scene/build settings or MatchManager prefab are missing."); continue; }
            Scene loaded = default;
            UnityEngine.Events.UnityAction<Scene, LoadSceneMode> capture = (scene, mode) =>
            {
                if (scene.path == room.sceneName || scene.name == room.sceneName)
                { loaded = scene; CapturePresentation(scene); }
            };
            SceneManager.sceneLoaded += capture;
            AsyncOperation operation = null;
            try { operation = SceneManager.LoadSceneAsync(room.sceneName, new LoadSceneParameters(LoadSceneMode.Additive, LocalPhysicsMode.Physics3D)); }
            catch (Exception e) { Debug.LogException(e); }
            if (operation != null)
            {
                pendingServerLoad = operation;
                operation.completed += _ =>
                {
                    SceneManager.sceneLoaded -= capture;
                    if ((room.Closing || generation != serverGeneration || !NetworkServer.active) && loaded.IsValid())
                        UnloadRoomScene(loaded);
                };
                yield return operation;
            }
            else SceneManager.sceneLoaded -= capture;
            if (generation != serverGeneration) yield break;
            room.Scene = loaded;
            if (room.Closing || !NetworkServer.active)
            { continue; }
            if (!loaded.IsValid()) { FailRoom(room, "Server arena load failed."); continue; }
            // Arena scenes contain geometry, spawn markers and local scripts only.
            // Networked world objects must be instantiated from registered prefabs.
            var mm = Instantiate(matchManager);
            SceneManager.MoveGameObjectToScene(mm.gameObject, loaded);
            room.matchManager = mm; room.Phase = MatchPhase.Playing;
            mm.Init(room); NetworkServer.Spawn(mm.gameObject);
            foreach (var conn in new List<NetworkConnectionToClient>(room.Players))
                if (!room.Closing) matchService.TrySpawn(room, conn);
        }
        if (generation == serverGeneration) loadingRooms = false;
    }
    internal void FailRoom(Room room, string reason)
    {
        Debug.LogError("[SimpleMatch] " + reason);
        foreach (var conn in new List<NetworkConnectionToClient>(room.Players))
        {
            NetworkServer.SetClientNotReady(conn);
            if (conn.identity != null) NetworkServer.RemovePlayerForConnection(conn, RemovePlayerOptions.Destroy);
            conn.Send(new MatchStatusMessage { matchId = room.roomId, phase = MatchPhase.Error, detail = reason });
        }
        room.Clear(); matchService?.Forget(room); CloseRoom(room);
    }
    internal void CloseRoom(Room room)
    {
        room.Closing = true;
        if (!room.Scene.IsValid() || !room.Scene.isLoaded) return;
        foreach (var root in room.Scene.GetRootGameObjects())
            foreach (var identity in root.GetComponentsInChildren<NetworkIdentity>(true))
                if (identity != null && identity.isServer) NetworkServer.Destroy(identity.gameObject);
        UnloadRoomScene(room.Scene);
    }
    void UnloadRoomScene(Scene scene)
    {
        presentation.Remove(scene); renderers.Remove(scene); visibleScenes.Remove(scene);
        if (scene.IsValid() && scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
    }
    void CapturePresentation(Scene scene)
    {
        var bs = new List<BehaviourState>(); var rs = new List<RendererState>();
        foreach (var root in scene.GetRootGameObjects())
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true))
                if (r.GetComponentInParent<NetworkIdentity>() == null) { rs.Add(new RendererState { component = r, enabled = r.enabled }); r.enabled = false; }
            foreach (var b in root.GetComponentsInChildren<Behaviour>(true))
                if (b is Light || b is AudioSource || b is Camera || b is AudioListener)
                { bs.Add(new BehaviourState { component = b, enabled = b.enabled }); b.enabled = false; }
        }
        presentation[scene] = bs; renderers[scene] = rs; visibleScenes[scene] = false;
    }
    public override void LateUpdate()
    {
        base.LateUpdate();
        if (!NetworkServer.active || matchService == null) return;
        var localRoom = NetworkServer.localConnection != null ? matchService.FindRoomByConnection(NetworkServer.localConnection) : null;
        foreach (var room in matchService.Rooms)
        {
            bool show = room == localRoom && ClientPhase == MatchPhase.Playing;
            if (!visibleScenes.TryGetValue(room.Scene, out bool wasVisible) || wasVisible == show) continue;
            visibleScenes[room.Scene] = show;
            if (renderers.TryGetValue(room.Scene, out var rs))
                for (int i = 0; i < rs.Count; i++)
                {
                    var r = rs[i]; if (r.component == null) continue;
                    if (!show) { r.enabled = r.component.enabled; rs[i] = r; }
                    r.component.enabled = show && r.enabled;
                }
            if (presentation.TryGetValue(room.Scene, out var bs))
                for (int i = 0; i < bs.Count; i++)
                {
                    var b = bs[i]; if (b.component == null) continue;
                    if (!show) { b.enabled = b.component.enabled; bs[i] = b; }
                    b.component.enabled = show && b.enabled;
                }
        }
    }
    public override void OnStartClient()
    {
        NetworkClient.RegisterHandler<OnJoinRoomMessage>(OnJoinRoom);
        NetworkClient.RegisterHandler<MatchStatusMessage>(OnMatchStatus);
        NetworkClient.RegisterHandler<KillFeedMessage>(message => { if (message.matchId == ClientMatchId) KillFeedReceived?.Invoke(message); });
    }
    public override void OnClientConnect() { Status(MatchPhase.None, "Connected. Press Play."); }
    public override void OnClientDisconnect() { ResetClient(); Status(MatchPhase.None, "Disconnected"); }
    public override void OnClientError(TransportError error, string reason) { Status(MatchPhase.Error, reason); }
    public override void OnStopClient() { ResetClient(); }
    void ResetClient()
    {
        clientGeneration++; loadingClient = false; ClientMatchId = Guid.Empty;
        if (clientArena.IsValid() && clientArena.isLoaded) pendingClientLoad = SceneManager.UnloadSceneAsync(clientArena);
        clientArena = default;
    }
    void OnJoinRoom(OnJoinRoomMessage message)
    {
        ClientMatchId = message.matchId; Status(MatchPhase.Loading, "Loading arena...");
        StartCoroutine(LoadClientArena(message, clientGeneration));
    }
    IEnumerator LoadClientArena(OnJoinRoomMessage message, int generation)
    {
        // Host reuses the authoritative room scene; never creates another physics copy.
        if (!NetworkServer.active)
        {
            while (loadingClient || (pendingClientLoad != null && !pendingClientLoad.isDone)) yield return null;
            if (generation != clientGeneration || ClientMatchId != message.matchId) yield break;
            if (!clientArena.IsValid() || !clientArena.isLoaded)
            {
                if (!Application.CanStreamedLevelBeLoaded(message.sceneName))
                { Status(MatchPhase.Error, "Arena missing from build."); NetworkClient.Send(new LeaveRoomMessage()); yield break; }
                loadingClient = true;
                pendingClientLoad = SceneManager.LoadSceneAsync(message.sceneName, LoadSceneMode.Additive);
                pendingClientLoad.completed += _ =>
                {
                    if (generation == clientGeneration) return;
                    var stale = SceneManager.GetSceneByPath(message.sceneName);
                    if (!stale.IsValid()) stale = SceneManager.GetSceneByName(message.sceneName);
                    if (stale.IsValid() && stale.isLoaded) pendingClientLoad = SceneManager.UnloadSceneAsync(stale);
                };
                yield return pendingClientLoad;
                if (generation != clientGeneration) yield break;
                var loaded = SceneManager.GetSceneByPath(message.sceneName);
                if (!loaded.IsValid()) loaded = SceneManager.GetSceneByName(message.sceneName);
                loadingClient = false;
                if (generation != clientGeneration)
                { if (loaded.IsValid()) SceneManager.UnloadSceneAsync(loaded); yield break; }
                clientArena = loaded;
            }
        }
        if (!NetworkClient.isConnected || generation != clientGeneration || ClientMatchId != message.matchId) yield break;
        if (!NetworkClient.ready) NetworkClient.Ready();
        NetworkClient.Send(new ClientReadyMsg { matchid = message.matchId });
    }
    void OnMatchStatus(MatchStatusMessage message)
    {
        if (message.matchId != ClientMatchId) return;
        if (message.phase == MatchPhase.None || message.phase == MatchPhase.Error)
        { ClientMatchId = Guid.Empty; NetworkClient.ready = false; }
        Status(message.phase, message.detail);
    }
    public void ClickJoinRoom()
    {
        if (!NetworkClient.isConnected) { Status(MatchPhase.Error, "Connect first."); return; }
        if (ClientMatchId != Guid.Empty || ClientPhase == MatchPhase.Loading || ClientPhase == MatchPhase.Leaving) return;
        Status(MatchPhase.Loading, "Finding match..."); NetworkClient.Send(new JoinOrCreateRoomMessage());
    }
    public void ClickLeaveRoom()
    {
        if (!NetworkClient.isConnected || ClientMatchId == Guid.Empty) return;
        Status(MatchPhase.Leaving, "Leaving match...");
        // Stop local Command producers immediately. Waiting for the server's NotReady
        // response leaves a short window where movement/combat can be queued after Leave.
        NetworkClient.ready = false;
        NetworkClient.Send(new LeaveRoomMessage());
    }
}
