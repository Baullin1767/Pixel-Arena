using System;
using System.Collections.Generic;
using Mirror;
using PixelArena;
using UnityEngine;
using UnityEngine.SceneManagement;
[DefaultExecutionOrder(1000)]
[RequireComponent(typeof(NetworkMatch))]
public class MatchManager : NetworkBehaviour
{
    [SyncVar, NonSerialized] public Guid MatchId;
    [SyncVar] public MatchPhase Phase;
    public Room Room { get; private set; }
    public PhysicsScene RoomPhysics => Room.PhysicsScene;
    [SerializeField] GameObject playerPrefab;
    readonly List<Transform> spawnPoints = new();
    int nextSpawn;
    public event Action<NetworkIdentity> ServerPlayerSpawned;
    [Server] internal void Init(Room room)
    {
        Room = room; MatchId = room.roomId; Phase = MatchPhase.Playing;
        GetComponent<NetworkMatch>().matchId = MatchId;
        foreach (var root in room.Scene.GetRootGameObjects())
        {
            foreach (var point in root.GetComponentsInChildren<ArenaSpawnPoint>(true)) spawnPoints.Add(point.transform);
            if (spawnPoints.Count == 0 && root.name == "SpawnPoints")
                foreach (Transform point in root.transform) spawnPoints.Add(point);
        }
    }
    [Server] public bool TryGetSpawnPoint(out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.up * 2; rotation = Quaternion.identity;
        if (spawnPoints.Count == 0) return false;
        var point = spawnPoints[nextSpawn++ % spawnPoints.Count];
        position = point.position; rotation = point.rotation; return true;
    }
    [Server] public void PrepareRoomObject(GameObject obj)
    {
        SceneManager.MoveGameObjectToScene(obj, Room.Scene);
        if (obj.TryGetComponent<NetworkMatch>(out var match)) match.matchId = MatchId;
        if (obj.TryGetComponent<RoomMember>(out var member)) member.ServerAssign(Room);
    }
    [Server] public bool SpawnPlayer(NetworkConnectionToClient conn)
    {
        if (Room == null || Room.Closing || !Room.ContainsPlayer(conn)) return false;
        if (conn.identity != null) return true;
        var prefab = playerPrefab != null ? playerPrefab : NetworkManager.singleton.playerPrefab;
        if (prefab == null || prefab.GetComponent<RoomMember>() == null || prefab.GetComponent<NetworkMatch>() == null
            || !TryGetSpawnPoint(out var position, out var rotation)) return false;
        var player = Instantiate(prefab, position, rotation);
        PrepareRoomObject(player);
        NetworkServer.AddPlayerForConnection(conn, player);
        ServerPlayerSpawned?.Invoke(player.GetComponent<NetworkIdentity>());
        return true;
    }
    [Server] public bool ServerRespawn(NetworkIdentity player)
    {
        if (player == null || !player.TryGetComponent<RoomMember>(out var member) || member.Room != Room
            || !TryGetSpawnPoint(out var position, out var rotation)) return false;
        player.transform.SetPositionAndRotation(position, rotation);
        foreach (var component in player.GetComponents<MonoBehaviour>())
            if (component is IServerRespawnListener listener) listener.OnServerRespawn(position, rotation);
        return true;
    }
    [Server] public void BroadcastKill(uint killerNetId, uint victimNetId, string cause)
    {
        var message = new KillFeedMessage { matchId = MatchId, killerNetId = killerNetId, victimNetId = victimNetId, cause = cause };
        foreach (var conn in Room.Players) if (conn.isReady) conn.Send(message);
    }
    // Run after authoritative motors/projectiles have supplied their fixed-step input.
    [ServerCallback] void FixedUpdate() { if (Room != null && !Room.Closing && RoomPhysics.IsValid()) RoomPhysics.Simulate(Time.fixedDeltaTime); }
}
