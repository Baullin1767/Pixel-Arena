using System;
using System.Collections.Generic;
using Mirror;
using PixelArena;
public class MatchService
{
    readonly Dictionary<Guid, Room> rooms = new();
    readonly NetManager manager;
    public IReadOnlyCollection<Room> Rooms => rooms.Values;
    public Action<Guid> OnCreateRoom;
    public MatchService(NetManager manager) { this.manager = manager; }
    internal void OnJoinCreateRoom(NetworkConnectionToClient conn, JoinOrCreateRoomMessage message)
    {
        if (FindRoomByConnection(conn) != null) return;
        var room = FindRoomWithFreeSlot() ?? CreateRoom(manager.roomCapacity);
        room.AddPlayer(conn);
        conn.Send(new OnJoinRoomMessage { matchId = room.roomId, sceneName = room.sceneName });
    }
    internal void OnServerLeaveRoom(NetworkConnectionToClient conn, LeaveRoomMessage msg = default)
    {
        var room = FindRoomByConnection(conn);
        if (room == null) return;
        NetworkServer.SetClientNotReady(conn);
        if (conn.identity != null) NetworkServer.RemovePlayerForConnection(conn, RemovePlayerOptions.Destroy);
        room.RemovePlayer(conn);
        conn.Send(new MatchStatusMessage { matchId = room.roomId, phase = MatchPhase.None, detail = "Left match" });
        if (room.IsEmpty) { rooms.Remove(room.roomId); manager.CloseRoom(room); }
    }
    internal void OnReadyPlayer(NetworkConnectionToClient conn, ClientReadyMsg msg)
    {
        var room = FindRoomByConnection(conn);
        if (room == null || room.roomId != msg.matchid || room.Closing) return;
        room.readyPlayers.Add(conn.connectionId);
        TrySpawn(room, conn);
    }
    internal void TrySpawn(Room room, NetworkConnectionToClient conn)
    {
        if (room.matchManager == null || !room.readyPlayers.Contains(conn.connectionId)) return;
        // Setting ready before AddPlayer is safe: Match AOI ignores connections without a player.
        NetworkServer.SetClientReady(conn);
        if (room.matchManager.SpawnPlayer(conn))
            conn.Send(new MatchStatusMessage { matchId = room.roomId, phase = MatchPhase.Playing, detail = "Playing" });
        else manager.FailRoom(room, "Player prefab or arena spawn points are missing.");
    }
    public Room FindRoomById(Guid id) => rooms.TryGetValue(id, out var room) ? room : null;
    public Room FindRoomByConnection(NetworkConnectionToClient conn)
    { foreach (var room in rooms.Values) if (room.ContainsPlayer(conn)) return room; return null; }
    public Room FindRoomWithFreeSlot()
    { foreach (var room in rooms.Values) if (room.HasFreeSlot) return room; return null; }
    public Room CreateRoom(int maxPlayers)
    {
        var room = new Room(maxPlayers, manager.arenaScene);
        rooms.Add(room.roomId, room); OnCreateRoom?.Invoke(room.roomId); return room;
    }
    internal void Forget(Room room) { rooms.Remove(room.roomId); }
    internal void Shutdown() { foreach (var room in new List<Room>(rooms.Values)) manager.CloseRoom(room); rooms.Clear(); }
}
