using System;
using Mirror;
using PixelArena;
public struct JoinOrCreateRoomMessage : NetworkMessage { }
public struct LeaveRoomMessage : NetworkMessage { }
public struct ClientReadyMsg : NetworkMessage { public Guid matchid; }
public struct OnJoinRoomMessage : NetworkMessage { public Guid matchId; public string sceneName; }
public struct MatchStatusMessage : NetworkMessage { public Guid matchId; public MatchPhase phase; public string detail; }
public struct KillFeedMessage : NetworkMessage { public Guid matchId; public uint killerNetId; public uint victimNetId; public string cause; }
