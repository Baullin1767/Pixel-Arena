using System;
using UnityEngine;

namespace PixelArena
{
    public enum WeaponKind : byte
    {
        Pistol,
        Rifle,
        RocketLauncher,
        RicochetGun,
        GrenadeLauncher
    }

    public enum WeaponFireMode : byte
    {
        Hitscan,
        Rocket,
        Ricochet,
        Grenade
    }

    [Serializable]
    public sealed class WeaponSettings
    {
        public WeaponKind kind;
        public string displayName;
        public WeaponFireMode fireMode;
        public bool automatic;
        [Min(1)] public int magazineSize = 1;
        [Min(0)] public int startingReserve;
        [Min(0.01f)] public float shotInterval = 0.25f;
        [Min(0.01f)] public float reloadDuration = 1.2f;
        [Min(0f)] public float damage = 10f;
        [Min(0.1f)] public float range = 100f;
        [Min(0.1f)] public float projectileSpeed = 20f;
        [Min(0f)] public float projectileGravity;
        [Min(0.01f)] public float projectileRadius = 0.12f;
        [Min(0f)] public float capsuleHalfLength = 0.3f;
        [Min(0f)] public float explosionRadius;
        [Min(0.01f)] public float projectileMass = 0.45f;
        [Range(0f, 1f)] public float projectileBounciness = 0.6f;
        [Range(0f, 1f)] public float projectileFriction = 0.05f;
        [Range(0, 10)] public int maxBounces;

        public static WeaponSettings[] CreatePrototypeDefaults()
        {
            return new[]
            {
                new WeaponSettings
                {
                    kind = WeaponKind.Pistol, displayName = "Pistol", fireMode = WeaponFireMode.Hitscan,
                    magazineSize = 12, startingReserve = 48, shotInterval = 0.25f,
                    reloadDuration = 1.2f, damage = 25f, range = 100f
                },
                new WeaponSettings
                {
                    kind = WeaponKind.Rifle, displayName = "Rifle", fireMode = WeaponFireMode.Hitscan,
                    automatic = true, magazineSize = 30, startingReserve = 120, shotInterval = 0.12f,
                    reloadDuration = 1.6f, damage = 12f, range = 120f
                },
                new WeaponSettings
                {
                    kind = WeaponKind.RocketLauncher, displayName = "Rocket Launcher", fireMode = WeaponFireMode.Rocket,
                    magazineSize = 1, startingReserve = 5, shotInterval = 0.8f, reloadDuration = 1.8f,
                    damage = 100f, range = 80f, projectileSpeed = 22f, projectileRadius = 0.16f,
                    explosionRadius = 4f
                },
                new WeaponSettings
                {
                    kind = WeaponKind.RicochetGun, displayName = "Ricochet Gun", fireMode = WeaponFireMode.Ricochet,
                    automatic = true, magazineSize = 20, startingReserve = 80, shotInterval = 0.15f,
                    reloadDuration = 1.7f, damage = 18f, range = 120f, maxBounces = 10
                },
                new WeaponSettings
                {
                    kind = WeaponKind.GrenadeLauncher, displayName = "Grenade Launcher", fireMode = WeaponFireMode.Grenade,
                    magazineSize = 4, startingReserve = 12, shotInterval = 0.7f, reloadDuration = 1.7f,
                    damage = 75f, range = 60f, projectileSpeed = 16f, projectileGravity = 18f,
                    projectileRadius = 0.18f, capsuleHalfLength = 0.35f, explosionRadius = 4f,
                    projectileMass = 0.45f, projectileBounciness = 0.65f, projectileFriction = 0.05f
                }
            };
        }
    }

    public readonly struct DamageInfo
    {
        public readonly float Amount;
        public readonly uint KillerNetId;
        public readonly RoomMember SourceRoom;
        public readonly string Cause;
        public readonly Vector3 Point;
        public readonly Vector3 Direction;

        public DamageInfo(float amount, uint killerNetId, RoomMember sourceRoom, string cause,
            Vector3 point, Vector3 direction)
        {
            Amount = amount;
            KillerNetId = killerNetId;
            SourceRoom = sourceRoom;
            Cause = cause;
            Point = point;
            Direction = direction;
        }
    }

    /// <summary>Server-only damage contract used by weapons and world hazards.</summary>
    public interface IServerDamageable
    {
        bool IsAlive { get; }
        RoomMember Member { get; }
        bool ServerApplyDamage(DamageInfo damage);
        bool ServerKillEnvironment(string cause);
    }
}
