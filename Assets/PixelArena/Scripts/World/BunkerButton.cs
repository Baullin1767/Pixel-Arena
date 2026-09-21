using UnityEngine;
namespace PixelArena
{
    public sealed class BunkerButton : MonoBehaviour, IPlayerInteractable
    {
        public ArenaBunker bunker;
        public string InteractionPrompt => "E — activate bunker alarm";
        public bool CanServerInteract(PlayerMotor player)
        {
            var controller = player != null && player.Member != null && player.Member.MatchManager != null
                ? player.Member.MatchManager.GetComponent<BunkerController>() : null;
            return controller != null && controller.CanActivate(player, this);
        }
        public void ServerInteract(PlayerMotor player)
        {
            if (!CanServerInteract(player)) return;
            player.Member.MatchManager.GetComponent<BunkerController>().ServerActivate(player, this);
        }
    }
}