using Mirror;
namespace PixelArena
{
    public class RoomInterestManagement : MatchInterestManagement
    {
        public override bool OnCheckObserver(NetworkIdentity identity, NetworkConnectionToClient observer)
            => observer.identity != null && base.OnCheckObserver(identity, observer);
    }
}
