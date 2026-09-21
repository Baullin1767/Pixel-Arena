using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PixelArena
{
    public enum BunkerPhase : byte { Ready, Countdown, Cooldown }

    // Attach to the registered MatchManager prefab, before spawning.
    [RequireComponent(typeof(MatchManager))]
    public sealed class BunkerController : NetworkBehaviour
    {
        [Min(1)] public float countdownSeconds = 30f;
        [Min(1)] public float cooldownSeconds = 120f;
        [Min(.1f)] public float doorHoldSeconds = 2f;
        [Min(.1f)] public float activationRange = 3f;
        [SyncVar] public BunkerPhase Phase;
        [SyncVar] public double PhaseEndsAt;
        [SyncVar] public double DoorsOpenAt;
        [SyncVar] public uint DetonationCount;
        public double RemainingSeconds => Math.Max(0, PhaseEndsAt - NetworkTime.time);
        public bool DoorsClosed => Phase == BunkerPhase.Cooldown && NetworkTime.time < DoorsOpenAt;
        public ArenaBunker Arena => arena;
        public event Action StateChanged;

        MatchManager match;
        ArenaBunker arena;
        AudioSource siren;
        AudioClip sirenClip;
        bool lastClosed;
        BunkerPhase lastPhase;
        double lastEnd;
        uint lastDetonation;
        static readonly List<BunkerController> instances = new();

        public static BunkerController Local
        {
            get
            {
                var member = NetworkClient.localPlayer != null ? NetworkClient.localPlayer.GetComponent<RoomMember>() : null;
                if (member == null || member.MatchId == Guid.Empty) return null;
                foreach (var value in instances)
                    if (value != null && value.match != null && value.match.MatchId == member.MatchId) return value;
                return null;
            }
        }
        void Awake() { match = GetComponent<MatchManager>(); }
        void Register() { if (!instances.Contains(this)) instances.Add(this); }
        public override void OnStartServer() { Register(); BindArena(); }
        public override void OnStartClient() { Register(); }
        public override void OnStopClient() { StopSiren(); }
        void OnDestroy()
        {
            instances.Remove(this);
            StopSiren();
            if (sirenClip != null) Destroy(sirenClip);
        }
        void StopSiren() { if (siren != null) siren.Stop(); }
        void BindArena()
        {
            if (arena != null) return;
            if (isServer)
            {
                if (match.Room == null || !match.Room.Scene.IsValid()) return;
                arena = FindArena(match.Room.Scene);
            }
            else if (Local == this)
            {
                for (int i = 0; i < SceneManager.sceneCount && arena == null; i++)
                    arena = FindArena(SceneManager.GetSceneAt(i));
            }
        }
        static ArenaBunker FindArena(Scene scene)
        {
            if (!scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = root.GetComponentInChildren<ArenaBunker>(true);
                if (found != null) return found;
            }
            return null;
        }
        [Server]
        public bool CanActivate(PlayerMotor player, BunkerButton target)
        {
            BindArena();
            return Phase == BunkerPhase.Ready && match.Room != null && !match.Room.Closing
                && match.Room.Phase == MatchPhase.Playing && player != null && player.IsAlive && player.MovementEnabled
                && player.Member != null && player.Member.Room == match.Room
                && player.gameObject.scene == match.Room.Scene && target != null && target.isActiveAndEnabled
                && target.gameObject.scene == match.Room.Scene && arena != null && target.bunker == arena
                && arena.button == target.transform
                && Vector3.Distance(player.ServerEyePosition, target.transform.position) <= activationRange;
        }
        [Server]
        public void ServerActivate(PlayerMotor player, BunkerButton target)
        {
            if (!CanActivate(player, target)) return;
            Phase = BunkerPhase.Countdown;
            PhaseEndsAt = NetworkTime.time + Mathf.Max(1, countdownSeconds);
        }
        [Server]
        void Detonate(double now)
        {
            // Change phase before damage: a respawn or later frame cannot trigger the same blast again.
            Phase = BunkerPhase.Cooldown;
            PhaseEndsAt = now + Mathf.Max(1, cooldownSeconds);
            DoorsOpenAt = now + Mathf.Min(Mathf.Max(.1f, doorHoldSeconds), Mathf.Max(1, cooldownSeconds));
            DetonationCount++;
            arena.SetDoorClosed(true);
            foreach (var connection in match.Room.Players)
            {
                var identity = connection.identity;
                if (identity == null) continue;
                var combat = identity.GetComponent<PlayerCombat>();
                var motor = identity.GetComponent<PlayerMotor>();
                if (combat == null || motor == null || !combat.IsAlive || combat.Member.Room != match.Room
                    || identity.gameObject.scene != match.Room.Scene || arena.ContainsPlayer(motor)) continue;
                combat.ServerKillEnvironment("Bunker");
            }
        }
        void Update()
        {
            BindArena();
            if (isServer && arena != null && match.Room != null && !match.Room.Closing)
            {
                double now = NetworkTime.time;
                if (Phase == BunkerPhase.Countdown && now >= PhaseEndsAt) Detonate(now);
                else if (Phase == BunkerPhase.Cooldown && now >= PhaseEndsAt)
                { Phase = BunkerPhase.Ready; PhaseEndsAt = 0; DoorsOpenAt = 0; }
            }
            if (arena != null && (isServer || Local == this)) arena.SetDoorClosed(DoorsClosed);
            bool audible = isClient && Local == this && Phase == BunkerPhase.Countdown;
            if (audible)
            {
                EnsureSiren();
                if (!siren.isPlaying) siren.Play();
            }
            else StopSiren();
            if (lastPhase != Phase || lastEnd != PhaseEndsAt || lastClosed != DoorsClosed || lastDetonation != DetonationCount)
            {
                lastPhase = Phase; lastEnd = PhaseEndsAt; lastClosed = DoorsClosed; lastDetonation = DetonationCount;
                StateChanged?.Invoke();
            }
        }
        void EnsureSiren()
        {
            if (siren != null) { siren.enabled = true; return; }
            // Audio stays on this room-scoped network object, never static visibility-captured geometry.
            siren = gameObject.AddComponent<AudioSource>();
            siren.playOnAwake = false; siren.loop = true; siren.spatialBlend = 0; siren.volume = .12f;
            const int rate = 22050;
            var samples = new float[rate * 2];
            double angle = 0;
            for (int i = 0; i < samples.Length; i++)
            {
                double frequency = 520 + 180 * Math.Sin(2 * Math.PI * i / rate);
                angle += 2 * Math.PI * frequency / rate;
                samples[i] = (float)Math.Sin(angle) * .5f;
            }
            sirenClip = AudioClip.Create("Bunker siren", samples.Length, 1, rate, false);
            sirenClip.SetData(samples, 0); siren.clip = sirenClip;
        }
    }
}