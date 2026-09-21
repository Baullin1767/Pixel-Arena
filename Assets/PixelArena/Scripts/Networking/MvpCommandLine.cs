using System;
using System.Collections;
using Mirror;
using UnityEngine;
namespace PixelArena
{
    public sealed class MvpCommandLine : MonoBehaviour
    {
        static bool booted;
        bool serverProbe;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (booted) return;
            var args = Environment.GetCommandLineArgs();
            if (NetworkManager.singleton is NetManager manager)
            {
                int capacity = Array.IndexOf(args, "--room-capacity");
                if (capacity >= 0 && capacity + 1 < args.Length && int.TryParse(args[capacity + 1], out int value))
                    manager.roomCapacity = Mathf.Max(1, value);
            }
            bool client = Array.IndexOf(args, "--client") >= 0;
            bool server = Array.IndexOf(args, "--validation-server") >= 0;
            if (!client && !server) return;
            booted = true;
            var probe = new GameObject("Command line validation").AddComponent<MvpCommandLine>();
            probe.serverProbe = server;
        }
        IEnumerator Start()
        {
            DontDestroyOnLoad(gameObject);
            if (serverProbe)
            {
                while (!NetworkServer.active || !(NetworkManager.singleton is NetManager activeManager)
                    || !RoomsReady(activeManager)) yield return null;
                var serverManager = (NetManager)NetworkManager.singleton;
                foreach (var room in serverManager.matchService.Rooms)
                    Debug.Log($"[PixelArena validation] Server room {room.roomId}; players {room.PlayerCount}; physics {room.PhysicsScene.IsValid()}.");
                yield break;
            }
            var args = Environment.GetCommandLineArgs();
            int i = Array.IndexOf(args, "--client");
            while (NetworkManager.singleton == null) yield return null;
            var manager = (NetManager)NetworkManager.singleton;
            manager.networkAddress = i + 1 < args.Length ? args[i + 1] : "127.0.0.1";
            // Startup after Mirror's headless auto-start. Clients should run without -batchmode.
            manager.StartClient();
            while (!NetworkClient.isConnected) yield return null;
            manager.ClickJoinRoom();
            while (manager.ClientPhase != MatchPhase.Playing) yield return null;
            Debug.Log($"[PixelArena validation] Client joined match {manager.ClientMatchId}; local player {NetworkClient.localPlayer?.netId}.");
            Destroy(gameObject);
        }

        static bool RoomsReady(NetManager manager)
        {
            if (manager.matchService == null || manager.matchService.Rooms.Count < 2) return false;
            foreach (var room in manager.matchService.Rooms)
                if (!room.Scene.IsValid() || !room.Scene.isLoaded || room.PlayerCount != 1
                    || room.matchManager == null || room.Players[0].identity == null) return false;
            return true;
        }
    }
}
