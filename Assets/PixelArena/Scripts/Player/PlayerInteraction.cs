using UnityEngine;
namespace PixelArena
{
    /// <summary>Server-authoritative movement contract consumed by combat/death systems.</summary>
    public interface IPlayerMotorAuthority
    {
        bool IsAlive { get; }
        bool MovementEnabled { get; }
        RoomMember Member { get; }
        Vector3 ServerEyePosition { get; }
        Vector3 ServerAimDirection { get; }
        Ray ServerAimRay { get; }
        void ServerSetAlive(bool value);
        void ServerSetMovementEnabled(bool value);
        void ServerTeleport(Vector3 position, Quaternion rotation);
    }

    /// <summary>Called only after the motor validates room, distance, line of sight and alive state.
    /// Implementations must also validate their own cooldown/state on the server.</summary>
    public interface IPlayerInteractable
    {
        string InteractionPrompt { get; }
        bool CanServerInteract(PlayerMotor player);
        void ServerInteract(PlayerMotor player);
    }
}
