using System;
using Mirror;
using UnityEngine;
namespace PixelArena
{
    public enum MatchPhase : byte { None, Loading, Playing, Leaving, Error }
    // Implement on a player's motor/combat component to reset authoritative state after teleport.
    public interface IServerRespawnListener { void OnServerRespawn(Vector3 position, Quaternion rotation); }
    [RequireComponent(typeof(NetworkIdentity), typeof(NetworkMatch))]
    public class RoomMember : NetworkBehaviour
    {
        [SyncVar, NonSerialized] public Guid MatchId;
        [SyncVar] public MatchPhase Phase;
        public Room Room { get; private set; }
        public MatchManager MatchManager => Room?.matchManager;
        [Server] public void ServerAssign(Room room)
        {
            Room = room; MatchId = room.roomId; Phase = room.Phase;
            GetComponent<NetworkMatch>().matchId = room.roomId;
        }
        public bool SharesRoom(RoomMember other) => other != null && MatchId != Guid.Empty && MatchId == other.MatchId;
    }
}
