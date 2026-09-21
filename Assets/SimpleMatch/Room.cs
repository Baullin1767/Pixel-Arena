using System;
using System.Collections.Generic;
using Mirror;
using PixelArena;
using UnityEngine;
using UnityEngine.SceneManagement;
public class Room
{
    public readonly Guid roomId = Guid.NewGuid();
    public readonly int maxPlayers;
    public readonly string sceneName;
    readonly List<NetworkConnectionToClient> players = new();
    internal readonly HashSet<int> readyPlayers = new();
    public IReadOnlyList<NetworkConnectionToClient> Players => players;
    public int PlayerCount => players.Count;
    public bool HasFreeSlot => !Closing && players.Count < maxPlayers;
    public bool IsEmpty => players.Count == 0;
    public MatchManager matchManager;
    public Scene Scene { get; internal set; }
    public PhysicsScene PhysicsScene => Scene.GetPhysicsScene();
    public MatchPhase Phase { get; internal set; } = MatchPhase.Loading;
    public bool Closing { get; internal set; }
    public Room(int maxPlayers, string sceneName = "Arena") { this.maxPlayers = Mathf.Max(1, maxPlayers); this.sceneName = sceneName; }
    public bool ContainsPlayer(NetworkConnectionToClient conn) => players.Contains(conn);
    public bool AddPlayer(NetworkConnectionToClient conn) { if (!HasFreeSlot || players.Contains(conn)) return false; players.Add(conn); return true; }
    public bool RemovePlayer(NetworkConnectionToClient conn) { readyPlayers.Remove(conn.connectionId); return players.Remove(conn); }
    public void Clear() { players.Clear(); readyPlayers.Clear(); }
}
