using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using Mirror;
using Mirror.SimpleWeb;
using UnityEngine;
using UnityEngine.Profiling;
using Debug = UnityEngine.Debug;
using Random = System.Random;

namespace PixelArena
{
    public sealed class StressTestConfig
    {
        public enum RunMode { None, Server, Runner, Bot }

        public RunMode Mode;
        public int Bots = 100;
        public int Rooms = 20;
        public int BotId;
        public int Port = 7777;
        public int BotFps = 30;
        public float DurationSeconds = 600f;
        public float SpawnRate = 10f;
        public float TelemetryInterval = 30f;
        public float MinSessionSeconds = 30f;
        public float MaxSessionSeconds = 90f;
        public string Address = "127.0.0.1";
        public string RunId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);

        public int RoomCapacity => Mathf.Max(1, Mathf.CeilToInt((float)Bots / Mathf.Max(1, Rooms)));

        public static StressTestConfig Parse(string[] args)
        {
            var config = new StressTestConfig();
            if (Has(args, "--stress-server")) config.Mode = RunMode.Server;
            else if (Has(args, "--stress-runner")) config.Mode = RunMode.Runner;
            else if (Has(args, "--stress-bot")) config.Mode = RunMode.Bot;
            config.Bots = Int(args, "--bots", config.Bots, 1, 10000);
            config.Rooms = Int(args, "--rooms", config.Rooms, 1, config.Bots);
            config.BotId = Int(args, "--bot-id", config.BotId, 0, 1000000);
            config.Port = Int(args, "--port", config.Port, 1, ushort.MaxValue);
            config.BotFps = Int(args, "--bot-fps", config.BotFps, 10, 240);
            config.DurationSeconds = Float(args, "--duration", config.DurationSeconds, 0f, 86400f);
            config.SpawnRate = Float(args, "--spawn-rate", config.SpawnRate, 0.1f, 1000f);
            config.TelemetryInterval = Float(args, "--telemetry-interval", config.TelemetryInterval, 5f, 600f);
            config.MinSessionSeconds = Float(args, "--min-session", config.MinSessionSeconds, 5f, 3600f);
            config.MaxSessionSeconds = Float(args, "--max-session", config.MaxSessionSeconds,
                config.MinSessionSeconds, 7200f);
            config.Address = Text(args, "--address", config.Address);
            config.RunId = Text(args, "--run-id", config.RunId);
            return config;
        }

        static bool Has(string[] args, string name) => Array.IndexOf(args, name) >= 0;
        static string Text(string[] args, string name, string fallback)
        {
            int index = Array.IndexOf(args, name);
            return index >= 0 && index + 1 < args.Length && !string.IsNullOrWhiteSpace(args[index + 1])
                ? args[index + 1] : fallback;
        }
        static int Int(string[] args, string name, int fallback, int min, int max)
        {
            return int.TryParse(Text(args, name, null), NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
                ? Mathf.Clamp(value, min, max) : fallback;
        }
        static float Float(string[] args, string name, float fallback, float min, float max)
        {
            return float.TryParse(Text(args, name, null), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? Mathf.Clamp(value, min, max) : fallback;
        }
    }

    [DefaultExecutionOrder(-10000)]
    public sealed class StressTestRuntime : MonoBehaviour
    {
        sealed class BotState
        {
            public int BotId;
            public int Generation;
            public int ConnectionId;
            public float Fps;
            public float FrameTimeMs;
            public double LastTelemetry;
            public Guid MatchId;
            public MatchPhase Phase;
        }

        static readonly Dictionary<int, BotState> BotsByConnection = new();
        static readonly Dictionary<int, BotState> BotsById = new();
        static readonly HashSet<int> SeenBotIds = new();
        static StressTestConfig config;
        static StressTestRuntime instance;
        static int joins;
        static int leaves;
        static int reconnects;
        static int failures;

        readonly List<Process> children = new();
        Random random;
        float startedAt;
        float nextTelemetry;
        int botGeneration;
        int sampledFrames;
        float sampledTime;
        TimeSpan lastCpu;
        float lastCpuAt;

        public static bool IsBotProcess => config != null && config.Mode == StressTestConfig.RunMode.Bot;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (instance != null) return;
            config = StressTestConfig.Parse(Environment.GetCommandLineArgs());
            if (config.Mode == StressTestConfig.RunMode.None) return;
            if (!(NetworkManager.singleton is NetManager manager))
            {
                Debug.LogError("[Stress] NetManager is missing from the startup scene.");
                return;
            }

            if (manager.transport is SimpleWebTransport web) web.port = (ushort)config.Port;
            if (config.Mode == StressTestConfig.RunMode.Server)
            {
                manager.roomCapacity = config.RoomCapacity;
                manager.maxConnections = Mathf.Max(manager.maxConnections, config.Bots + 16);
                manager.headlessStartMode = HeadlessStartOptions.AutoStartServer;
            }
            else manager.headlessStartMode = HeadlessStartOptions.DoNothing;

            instance = new GameObject("Stress Test Runtime").AddComponent<StressTestRuntime>();
            DontDestroyOnLoad(instance.gameObject);
        }

        void Awake()
        {
            random = new Random(unchecked(config.BotId * 397) ^ Environment.TickCount);
            startedAt = Time.realtimeSinceStartup;
            nextTelemetry = startedAt + config.TelemetryInterval;
            Application.runInBackground = true;
            if (config.Mode == StressTestConfig.RunMode.Bot)
            {
                Application.targetFrameRate = config.BotFps;
                QualitySettings.vSyncCount = 0;
            }
        }

        IEnumerator Start()
        {
            switch (config.Mode)
            {
                case StressTestConfig.RunMode.Server:
                    while (!NetworkServer.active) yield return null;
                    Debug.Log($"[Stress] START mode=server bots={config.Bots} rooms={config.Rooms} " +
                        $"capacity={config.RoomCapacity} duration={config.DurationSeconds:0}s port={config.Port}");
                    yield return LaunchBots();
                    break;
                case StressTestConfig.RunMode.Runner:
                    Debug.Log($"[Stress] START mode=runner bots={config.Bots} target={config.Address}:{config.Port}");
                    yield return LaunchBots();
                    break;
                case StressTestConfig.RunMode.Bot:
                    yield return BotLoop();
                    break;
            }
        }

        void Update()
        {
            sampledFrames++;
            sampledTime += Time.unscaledDeltaTime;
            if (config == null || config.Mode == StressTestConfig.RunMode.Bot) return;
            if (Time.realtimeSinceStartup >= nextTelemetry)
            {
                LogServerSummary();
                nextTelemetry = Time.realtimeSinceStartup + config.TelemetryInterval;
            }
            if (config.DurationSeconds > 0f && Time.realtimeSinceStartup - startedAt >= config.DurationSeconds)
            {
                LogServerSummary();
                Debug.Log("[Stress] COMPLETE duration reached.");
                enabled = false;
                StartCoroutine(QuitNextFrame());
            }
        }

        IEnumerator QuitNextFrame()
        {
            yield return null;
            Application.Quit(0);
        }

        IEnumerator LaunchBots()
        {
            if (Application.isEditor)
            {
                Debug.LogError("[Stress] Bot process launcher is supported in a standalone build only.");
                yield break;
            }
            string executable = Process.GetCurrentProcess().MainModule != null
                ? Process.GetCurrentProcess().MainModule.FileName : null;
            if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            {
                Debug.LogError("[Stress] Cannot resolve the current executable.");
                yield break;
            }
            string root = Path.Combine(Path.GetDirectoryName(executable) ?? ".", "StressLogs", config.RunId);
            Directory.CreateDirectory(root);
            float delay = 1f / config.SpawnRate;
            for (int id = 0; id < config.Bots; id++)
            {
                string log = Path.Combine(root, $"bot-{id:0000}.log");
                string args = $"-batchmode -nographics -logFile \"{log}\" --stress-bot --bot-id {id} " +
                    $"--address \"{config.Address}\" --port {config.Port} --duration {Invariant(config.DurationSeconds)} " +
                    $"--bot-fps {config.BotFps} --telemetry-interval {Invariant(config.TelemetryInterval)} " +
                    $"--min-session {Invariant(config.MinSessionSeconds)} --max-session {Invariant(config.MaxSessionSeconds)} " +
                    $"--run-id \"{config.RunId}\"";
                try
                {
                    var process = Process.Start(new ProcessStartInfo(executable, args)
                    {
                        WorkingDirectory = Path.GetDirectoryName(executable) ?? ".",
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    if (process != null) children.Add(process);
                    else failures++;
                }
                catch (Exception exception)
                {
                    failures++;
                    Debug.LogError($"[Stress] Failed to launch bot={id}: {exception.Message}");
                }
                if (delay > 0f) yield return new WaitForSecondsRealtime(delay);
            }
            Debug.Log($"[Stress] LAUNCHED processes={children.Count} failures={failures} logs={root}");
        }

        IEnumerator BotLoop()
        {
            while (NetworkManager.singleton == null) yield return null;
            var manager = (NetManager)NetworkManager.singleton;
            manager.networkAddress = config.Address;
            float stopAt = config.DurationSeconds > 0f ? Time.realtimeSinceStartup + config.DurationSeconds : float.PositiveInfinity;
            while (Time.realtimeSinceStartup < stopAt)
            {
                if (!NetworkClient.isConnected)
                {
                    botGeneration++;
                    manager.StartClient();
                    float connectDeadline = Time.realtimeSinceStartup + 20f;
                    while (!NetworkClient.isConnected && NetworkClient.active
                        && Time.realtimeSinceStartup < connectDeadline) yield return null;
                    if (!NetworkClient.isConnected)
                    {
                        failures++;
                        if (NetworkClient.active) manager.StopClient();
                        yield return new WaitForSecondsRealtime(Range(1f, 4f));
                        continue;
                    }
                    NetworkClient.Send(new StressBotHelloMessage { botId = config.BotId, generation = botGeneration });
                }

                if (manager.ClientPhase != MatchPhase.Playing) manager.ClickJoinRoom();
                float deadline = Time.realtimeSinceStartup + 30f;
                while (manager.ClientPhase != MatchPhase.Playing && NetworkClient.isConnected
                    && Time.realtimeSinceStartup < deadline) yield return null;
                if (manager.ClientPhase != MatchPhase.Playing)
                {
                    failures++;
                    manager.StopClient();
                    yield return new WaitForSecondsRealtime(Range(1f, 4f));
                    continue;
                }

                AttachAgent();
                float sessionEnd = Mathf.Min(stopAt, Time.realtimeSinceStartup
                    + Range(config.MinSessionSeconds, config.MaxSessionSeconds));
                while (NetworkClient.isConnected && manager.ClientPhase == MatchPhase.Playing
                    && Time.realtimeSinceStartup < sessionEnd)
                {
                    SampleAndSendTelemetry(manager);
                    if (NetworkClient.localPlayer != null
                        && NetworkClient.localPlayer.GetComponent<StressBotAgent>() == null) AttachAgent();
                    yield return null;
                }
                if (Time.realtimeSinceStartup >= stopAt) break;

                // Exercise both the room lifecycle and the transport lifecycle.
                if (random.NextDouble() < 0.55 && NetworkClient.isConnected)
                {
                    manager.ClickLeaveRoom();
                    deadline = Time.realtimeSinceStartup + 10f;
                    while (manager.ClientPhase != MatchPhase.None && NetworkClient.isConnected
                        && Time.realtimeSinceStartup < deadline) yield return null;
                    yield return new WaitForSecondsRealtime(Range(0.5f, 3f));
                    if (NetworkClient.isConnected) continue;
                }

                if (NetworkClient.active) manager.StopClient();
                while (NetworkClient.active) yield return null;
                yield return new WaitForSecondsRealtime(Range(1f, 6f));
            }
            if (NetworkClient.active) manager.StopClient();
            Debug.Log($"[StressBot {config.BotId}] COMPLETE generations={botGeneration} failures={failures}");
            Application.Quit(failures == 0 ? 0 : 2);
        }

        void AttachAgent()
        {
            var player = NetworkClient.localPlayer;
            if (player != null && player.GetComponent<StressBotAgent>() == null)
                player.gameObject.AddComponent<StressBotAgent>().Initialize(config.BotId);
        }

        void SampleAndSendTelemetry(NetManager manager)
        {
            sampledFrames++;
            sampledTime += Time.unscaledDeltaTime;
            if (Time.realtimeSinceStartup < nextTelemetry || sampledTime <= 0.001f) return;
            float fps = sampledFrames / sampledTime;
            NetworkClient.Send(new StressBotTelemetryMessage
            {
                botId = config.BotId,
                generation = botGeneration,
                fps = fps,
                frameTimeMs = 1000f / Mathf.Max(0.01f, fps),
                matchId = manager.ClientMatchId,
                phase = manager.ClientPhase
            });
            sampledFrames = 0;
            sampledTime = 0f;
            nextTelemetry = Time.realtimeSinceStartup + config.TelemetryInterval;
        }

        float Range(float min, float max) => min + (float)random.NextDouble() * Mathf.Max(0f, max - min);
        static string Invariant(float value) => value.ToString("0.###", CultureInfo.InvariantCulture);

        void LogServerSummary()
        {
            float fps = sampledTime > 0.001f ? sampledFrames / sampledTime : 0f;
            sampledFrames = 0;
            sampledTime = 0f;
            int roomCount = NetworkManager.singleton is NetManager manager && manager.matchService != null
                ? manager.matchService.Rooms.Count : 0;
            int playerCount = NetworkManager.singleton is NetManager active && active.matchService != null
                ? active.matchService.Rooms.Sum(room => room.PlayerCount) : 0;
            var current = Process.GetCurrentProcess();
            float now = Time.realtimeSinceStartup;
            TimeSpan cpu = current.TotalProcessorTime;
            float cpuPercent = lastCpuAt > 0f ? (float)((cpu - lastCpu).TotalSeconds
                / Math.Max(0.001f, now - lastCpuAt) / Math.Max(1, Environment.ProcessorCount) * 100.0) : 0f;
            lastCpu = cpu;
            lastCpuAt = now;
            long memory = Math.Max(current.WorkingSet64, Profiler.GetTotalAllocatedMemoryLong());
            float memoryMb = memory / (1024f * 1024f);
            var fresh = BotsById.Values.Where(bot => NetworkTime.time - bot.LastTelemetry <= config.TelemetryInterval * 2.5f)
                .Where(bot => bot.Fps > 0f)
                .OrderBy(bot => bot.BotId).ToArray();
            string fpsStats = fresh.Length == 0 ? "n/a" :
                $"avg={fresh.Average(bot => bot.Fps):0.0} min={fresh.Min(bot => bot.Fps):0.0} max={fresh.Max(bot => bot.Fps):0.0}";
            Debug.Log($"[Stress] STATUS connected={BotsByConnection.Count}/{config.Bots} players={playerCount} " +
                $"rooms={roomCount}/{config.Rooms} joins={joins} leaves={leaves} reconnects={reconnects} " +
                $"serverFps={fps:0.0} cpu={cpuPercent:0.0}% memory={memoryMb:0}MB botFps({fpsStats}) failures={failures}");
            if (fresh.Length > 0)
                Debug.Log("[Stress] FPS " + string.Join(" ", fresh.Select(bot => $"{bot.BotId}:{bot.Fps:0}")));
        }

        public static void ServerStarted()
        {
            NetworkServer.RegisterHandler<StressBotHelloMessage>(OnBotHello);
            NetworkServer.RegisterHandler<StressBotTelemetryMessage>(OnBotTelemetry);
        }

        public static void ServerStopped()
        {
            BotsByConnection.Clear();
            BotsById.Clear();
            SeenBotIds.Clear();
        }

        static void OnBotHello(NetworkConnectionToClient connection, StressBotHelloMessage message)
        {
            if (!SeenBotIds.Add(message.botId)) reconnects++;
            if (BotsById.TryGetValue(message.botId, out var previous) && previous.ConnectionId != connection.connectionId)
            {
                BotsByConnection.Remove(previous.ConnectionId);
            }
            var state = new BotState
            {
                BotId = message.botId,
                Generation = message.generation,
                ConnectionId = connection.connectionId,
                LastTelemetry = NetworkTime.time
            };
            BotsByConnection[connection.connectionId] = state;
            BotsById[message.botId] = state;
            Debug.Log($"[Stress] CONNECT bot={message.botId} generation={message.generation} connection={connection.connectionId}");
        }

        static void OnBotTelemetry(NetworkConnectionToClient connection, StressBotTelemetryMessage message)
        {
            if (!BotsByConnection.TryGetValue(connection.connectionId, out var state)
                || state.BotId != message.botId || state.Generation != message.generation) return;
            state.Fps = Mathf.Max(0f, message.fps);
            state.FrameTimeMs = Mathf.Max(0f, message.frameTimeMs);
            state.MatchId = message.matchId;
            state.Phase = message.phase;
            state.LastTelemetry = NetworkTime.time;
        }

        public static void ServerDisconnected(NetworkConnectionToClient connection)
        {
            if (!BotsByConnection.TryGetValue(connection.connectionId, out var state)) return;
            BotsByConnection.Remove(connection.connectionId);
            if (BotsById.TryGetValue(state.BotId, out var current) && current == state) BotsById.Remove(state.BotId);
            Debug.Log($"[Stress] DISCONNECT bot={state.BotId} generation={state.Generation} connection={connection.connectionId}");
        }

        public static void ServerRoomJoined(NetworkConnectionToClient connection, Room room)
        {
            joins++;
            if (BotsByConnection.TryGetValue(connection.connectionId, out var state))
            {
                state.MatchId = room.roomId;
                state.Phase = MatchPhase.Loading;
                Debug.Log($"[Stress] JOIN bot={state.BotId} room={room.roomId} players={room.PlayerCount}/{room.maxPlayers}");
            }
        }

        public static void ServerRoomLeft(NetworkConnectionToClient connection, Room room)
        {
            leaves++;
            if (BotsByConnection.TryGetValue(connection.connectionId, out var state))
            {
                state.MatchId = Guid.Empty;
                state.Phase = MatchPhase.None;
                Debug.Log($"[Stress] LEAVE bot={state.BotId} room={room.roomId} players={room.PlayerCount}/{room.maxPlayers}");
            }
        }

        void OnApplicationQuit()
        {
            for (int i = 0; i < children.Count; i++)
            {
                try { if (children[i] != null && !children[i].HasExited) children[i].Kill(); }
                catch { }
                finally { children[i]?.Dispose(); }
            }
            children.Clear();
        }
    }

    public sealed class StressBotAgent : MonoBehaviour
    {
        PlayerMotor motor;
        PlayerCombat combat;
        Random random;
        int botId;
        float nextDecision;
        float nextTargetScan;
        float nextJump;
        float nextInteract;
        float nextReload;
        float nextWeapon;
        Vector2 movement;
        byte weapon;
        Transform target;

        public void Initialize(int id)
        {
            botId = id;
            random = new Random(unchecked(id * 486187739) ^ Environment.TickCount);
            motor = GetComponent<PlayerMotor>();
            combat = GetComponent<PlayerCombat>();
            Decide();
        }

        void Update()
        {
            if (motor == null || combat == null || !motor.isLocalPlayer || !NetworkClient.ready) return;
            float now = Time.unscaledTime;
            if (now >= nextTargetScan) { FindTarget(); nextTargetScan = now + 1f; }
            if (now >= nextDecision) Decide();
            if (now >= nextWeapon) { weapon = (byte)random.Next(0, 5); nextWeapon = now + Range(5f, 14f); }
            bool jump = now >= nextJump;
            if (jump) nextJump = now + Range(2f, 7f);
            bool interact = now >= nextInteract;
            if (interact) nextInteract = now + Range(4f, 12f);
            bool reload = now >= nextReload;
            if (reload) nextReload = now + Range(6f, 16f);

            Vector3 aim = target != null ? target.position + Vector3.up - (transform.position + Vector3.up * motor.eyeHeight)
                : Quaternion.Euler(0f, botId * 37f + now * 18f, 0f) * Vector3.forward;
            if (aim.sqrMagnitude < 0.01f) aim = transform.forward;
            float yaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            float pitch = -Mathf.Asin(Mathf.Clamp(aim.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            bool hasTarget = target != null && combat.IsAlive;
            motor.SetBotInput(movement, yaw, pitch, jump, interact);
            combat.SetBotCombatInput(weapon, hasTarget, hasTarget && random.NextDouble() < 0.12, reload);
        }

        void Decide()
        {
            movement = Vector2.ClampMagnitude(new Vector2(Range(-1f, 1f), Range(-1f, 1f)), 1f);
            nextDecision = Time.unscaledTime + Range(0.8f, 3f);
        }

        void FindTarget()
        {
            target = null;
            float best = float.PositiveInfinity;
            var ownMember = GetComponent<RoomMember>();
            foreach (var pair in NetworkClient.spawned)
            {
                var identity = pair.Value;
                if (identity == null || identity.gameObject == gameObject) continue;
                var candidate = identity.GetComponent<PlayerCombat>();
                var member = identity.GetComponent<RoomMember>();
                if (candidate == null || !candidate.IsAlive || ownMember == null || member == null
                    || !ownMember.SharesRoom(member)) continue;
                float distance = (identity.transform.position - transform.position).sqrMagnitude;
                if (distance < best) { best = distance; target = identity.transform; }
            }
        }

        float Range(float min, float max) => min + (float)random.NextDouble() * (max - min);

        void OnDestroy()
        {
            if (motor != null) motor.ClearBotInput();
            if (combat != null) combat.ClearBotCombatInput();
        }
    }
}
