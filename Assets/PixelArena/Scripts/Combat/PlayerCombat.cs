using System;
using System.Collections.Generic;
using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PixelArena
{
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(NetworkIdentity), typeof(RoomMember), typeof(PlayerMotor))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PlayerCombat : NetworkBehaviour, IServerDamageable, IServerRespawnListener
    {
        const int WeaponCount = 5;

        [Header("Health and respawn")]
        [Min(1f)] public float maxHealth = 100f;
        [Min(0.1f)] public float respawnDelay = 3f;
        [Header("Weapons")]
        public WeaponSettings[] weapons = WeaponSettings.CreatePrototypeDefaults();
        [Tooltip("Registered network prefab containing NetworkIdentity, NetworkMatch, RoomMember and CombatProjectile.")]
        public GameObject projectilePrefab;
        public LayerMask hitMask = ~0;
        [Min(0.05f)] public float combatInputTimeout = 0.3f;

        [SyncVar(hook = nameof(OnHealthChanged))] float health;
        [SyncVar(hook = nameof(OnDeadChanged))] bool dead;
        [SyncVar(hook = nameof(OnSelectedWeaponChanged))] byte selectedWeapon;
        [SyncVar(hook = nameof(OnReloadChanged))] bool reloading;
        [SyncVar] byte reloadWeapon;
        [SyncVar] double reloadEndsAt;

        public readonly SyncList<int> Magazines = new();
        public readonly SyncList<int> Reserves = new();

        readonly double[] nextFireAt = new double[WeaponCount];
        readonly RaycastHit[] rayHits = new RaycastHit[64];
        readonly Renderer[] emptyRenderers = Array.Empty<Renderer>();
        readonly List<GameObject> ragdollPieces = new();
        IPlayerMotorAuthority motor;
        PlayerMotor concreteMotor;
        RoomMember member;
        CapsuleCollider bodyCollider;
        Renderer[] bodyRenderers;
        bool[] bodyRendererEnabled;
        byte requestedWeapon;
        bool localFirePressed;
        bool localReloadPressed;
        bool serverTriggerHeld;
        bool serverSemiQueued;
        double lastCombatInput = double.NegativeInfinity;
        float nextInputSend;
        double respawnAt = double.PositiveInfinity;

        public event Action StateChanged;
        public float Health => health;
        public float MaxHealth => maxHealth;
        public bool IsAlive => !dead;
        public bool IsDead => dead;
        public RoomMember Member => member;
        public int SelectedWeaponIndex => selectedWeapon;
        public WeaponSettings SelectedWeapon => GetWeapon(selectedWeapon);
        public int MagazineAmmo => GetAmmo(Magazines, selectedWeapon);
        public int ReserveAmmo => GetAmmo(Reserves, selectedWeapon);
        public bool IsReloading => reloading;
        public double ReloadEndsAt => reloadEndsAt;
        public float ReloadProgress
        {
            get
            {
                var weapon = GetWeapon(reloadWeapon);
                if (!reloading || weapon == null) return 0f;
                return 1f - Mathf.Clamp01((float)((reloadEndsAt - NetworkTime.time) / weapon.reloadDuration));
            }
        }

        void Reset() => weapons = WeaponSettings.CreatePrototypeDefaults();

        void Awake()
        {
            EnsureWeaponDefinitions();
            concreteMotor = GetComponent<PlayerMotor>();
            motor = concreteMotor;
            member = GetComponent<RoomMember>();
            bodyCollider = GetComponent<CapsuleCollider>();
            bodyRenderers = GetComponentsInChildren<Renderer>(true) ?? emptyRenderers;
            bodyRendererEnabled = new bool[bodyRenderers.Length];
            for (int i = 0; i < bodyRenderers.Length; i++)
                bodyRendererEnabled[i] = bodyRenderers[i] != null && bodyRenderers[i].enabled;
            Magazines.OnChange += OnAmmoChanged;
            Reserves.OnChange += OnAmmoChanged;
        }

        public override void OnStartServer()
        {
            ResetLoadout();
            health = Mathf.Max(1f, maxHealth);
            dead = false;
            reloading = false;
        }

        public override void OnStartClient()
        {
            ApplyDeathPresentation(dead);
            StateChanged?.Invoke();
        }

        public override void OnStartLocalPlayer()
        {
            requestedWeapon = selectedWeapon;
        }

        void Update()
        {
            if (!isLocalPlayer || !NetworkClient.ready) return;
            bool inputAllowed = concreteMotor != null && concreteMotor.LocalInputEnabled && !dead;
            var keyboard = Keyboard.current;
            var mouse = Mouse.current;
            if (inputAllowed && keyboard != null)
            {
                if (keyboard.digit1Key.wasPressedThisFrame) requestedWeapon = 0;
                else if (keyboard.digit2Key.wasPressedThisFrame) requestedWeapon = 1;
                else if (keyboard.digit3Key.wasPressedThisFrame) requestedWeapon = 2;
                else if (keyboard.digit4Key.wasPressedThisFrame) requestedWeapon = 3;
                else if (keyboard.digit5Key.wasPressedThisFrame) requestedWeapon = 4;
                localReloadPressed |= keyboard.rKey.wasPressedThisFrame;
            }
            bool triggerHeld = inputAllowed && mouse != null && mouse.leftButton.isPressed;
            if (inputAllowed && mouse != null) localFirePressed |= mouse.leftButton.wasPressedThisFrame;
            if (Time.unscaledTime < nextInputSend) return;
            nextInputSend = Time.unscaledTime + 1f / 30f;
            CmdCombatInput(requestedWeapon, triggerHeld, inputAllowed && localFirePressed,
                inputAllowed && localReloadPressed);
            localFirePressed = localReloadPressed = false;
        }

        [Command]
        void CmdCombatInput(byte requestedSlot, bool triggerHeld, bool firePressed, bool reloadPressed)
        {
            if (!CanFight()) return;
            lastCombatInput = NetworkTime.time;
            if (requestedSlot < WeaponCount && requestedSlot != selectedWeapon)
            {
                selectedWeapon = requestedSlot;
                CancelReload();
                serverSemiQueued = false;
            }
            serverTriggerHeld = triggerHeld;
            serverSemiQueued |= firePressed;
            if (reloadPressed) BeginReload();
        }

        [ServerCallback]
        void FixedUpdate()
        {
            if (dead)
            {
                if (NetworkTime.time >= respawnAt)
                {
                    respawnAt = double.PositiveInfinity;
                    if (member != null && member.MatchManager != null)
                        member.MatchManager.ServerRespawn(netIdentity);
                }
                return;
            }
            if (!CanFight())
            {
                serverTriggerHeld = serverSemiQueued = false;
                return;
            }
            if (reloading && NetworkTime.time >= reloadEndsAt) CompleteReload();
            var weapon = GetWeapon(selectedWeapon);
            if (weapon == null || reloading) return;
            bool inputFresh = NetworkTime.time - lastCombatInput <= combatInputTimeout;
            bool wantsShot = inputFresh && (weapon.automatic ? serverTriggerHeld : serverSemiQueued);
            serverSemiQueued = false;
            if (wantsShot) TryFire(weapon);
        }

        bool CanFight()
        {
            return !dead && motor != null && motor.IsAlive && motor.MovementEnabled && member != null
                && member.Room != null && !member.Room.Closing && member.Phase == MatchPhase.Playing
                && member.Room.PhysicsScene.IsValid();
        }

        [Server]
        bool TryFire(WeaponSettings weapon)
        {
            int slot = selectedWeapon;
            double now = NetworkTime.time;
            if (slot >= Magazines.Count || now < nextFireAt[slot]) return false;
            if ((weapon.fireMode != WeaponFireMode.Hitscan)
                && (projectilePrefab == null || projectilePrefab.GetComponent<CombatProjectile>() == null)) return false;
            if (Magazines[slot] <= 0)
            {
                BeginReload();
                return false;
            }

            Vector3 direction = motor.ServerAimDirection;
            if (!Finite(direction) || direction.sqrMagnitude < 0.99f) return false;
            direction.Normalize();
            Vector3 origin = motor.ServerEyePosition;
            Magazines[slot]--;
            nextFireAt[slot] = now + Mathf.Max(0.01f, weapon.shotInterval);

            switch (weapon.fireMode)
            {
                case WeaponFireMode.Hitscan:
                    FireHitscan(weapon, origin, direction);
                    break;
                case WeaponFireMode.Ricochet:
                    FireProjectile(weapon, origin, direction);
                    break;
                case WeaponFireMode.Rocket:
                case WeaponFireMode.Grenade:
                    FireProjectile(weapon, origin, direction);
                    break;
            }
            StateChanged?.Invoke();
            return true;
        }

        [Server]
        void FireHitscan(WeaponSettings weapon, Vector3 origin, Vector3 direction)
        {
            if (!TryNearestHit(origin, direction, weapon.range, out var hit)) return;
            var target = FindDamageable(hit.collider);
            target?.ServerApplyDamage(new DamageInfo(weapon.damage, netId, member, weapon.displayName,
                hit.point, direction));
        }

        [Server]
        void FireRicochet(WeaponSettings weapon, Vector3 origin, Vector3 direction)
        {
            float remaining = Mathf.Max(0.1f, weapon.range);
            int bounces = Mathf.Clamp(weapon.maxBounces, 0, 10);
            for (int segment = 0; segment <= bounces && remaining > 0.01f; segment++)
            {
                if (!TryNearestHit(origin, direction, remaining, out var hit)) return;
                remaining -= hit.distance;
                var target = FindDamageable(hit.collider);
                if (target != null)
                {
                    target.ServerApplyDamage(new DamageInfo(weapon.damage, netId, member, weapon.displayName,
                        hit.point, direction));
                    return;
                }
                direction = Vector3.Reflect(direction, hit.normal).normalized;
                origin = hit.point + hit.normal * 0.01f;
            }
        }

        [Server]
        void FireProjectile(WeaponSettings weapon, Vector3 origin, Vector3 direction)
        {
            var projectileObject = Instantiate(projectilePrefab, origin, Quaternion.LookRotation(direction));
            member.MatchManager.PrepareRoomObject(projectileObject);
            var projectile = projectileObject.GetComponent<CombatProjectile>();
            projectile.ServerInitialize(this, weapon, origin, direction, hitMask);
            NetworkServer.Spawn(projectileObject);
        }

        bool TryNearestHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            int count = member.Room.PhysicsScene.Raycast(origin, direction, rayHits, distance, hitMask,
                QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var hit = rayHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                var combat = hit.collider.GetComponentInParent<PlayerCombat>();
                if (combat != null && !combat.IsAlive) continue;
                if (hit.distance < best)
                {
                    best = hit.distance;
                    nearest = hit;
                }
            }
            return nearest.collider != null;
        }

        IServerDamageable FindDamageable(Collider hitCollider)
        {
            if (hitCollider == null) return null;
            foreach (var component in hitCollider.GetComponentsInParent<MonoBehaviour>())
            {
                if (component is IServerDamageable damageable && damageable.IsAlive
                    && damageable.Member != null && member.SharesRoom(damageable.Member)) return damageable;
            }
            return null;
        }

        [Server]
        void BeginReload()
        {
            int slot = selectedWeapon;
            var weapon = GetWeapon(slot);
            if (weapon == null || reloading || slot >= Magazines.Count || slot >= Reserves.Count
                || Magazines[slot] >= weapon.magazineSize || Reserves[slot] <= 0) return;
            reloading = true;
            reloadWeapon = (byte)slot;
            reloadEndsAt = NetworkTime.time + Mathf.Max(0.01f, weapon.reloadDuration);
            serverTriggerHeld = serverSemiQueued = false;
        }

        [Server]
        void CompleteReload()
        {
            int slot = reloadWeapon;
            var weapon = GetWeapon(slot);
            if (!reloading || weapon == null || slot >= Magazines.Count || slot >= Reserves.Count)
            {
                CancelReload();
                return;
            }
            int amount = Mathf.Min(weapon.magazineSize - Magazines[slot], Reserves[slot]);
            if (amount > 0)
            {
                Magazines[slot] += amount;
                Reserves[slot] -= amount;
            }
            CancelReload();
        }

        [Server]
        void CancelReload()
        {
            reloading = false;
            reloadEndsAt = 0;
        }

        [Server]
        public bool ServerApplyDamage(DamageInfo damage)
        {
            if (dead || damage.Amount <= 0f || float.IsNaN(damage.Amount) || float.IsInfinity(damage.Amount)
                || member == null || member.Room == null) return false;
            if (damage.KillerNetId != 0 && (damage.SourceRoom == null || !member.SharesRoom(damage.SourceRoom)))
                return false;
            health = Mathf.Max(0f, health - damage.Amount);
            if (health <= 0f) ServerDie(damage.KillerNetId, string.IsNullOrWhiteSpace(damage.Cause) ? "Damage" : damage.Cause);
            return true;
        }

        [Server]
        public bool ServerKillEnvironment(string cause)
        {
            if (dead) return false;
            health = 0f;
            ServerDie(0, string.IsNullOrWhiteSpace(cause) ? "Environment" : cause);
            return true;
        }

        [Server]
        void ServerDie(uint killerNetId, string cause)
        {
            if (dead) return;
            dead = true;
            health = 0f;
            respawnAt = NetworkTime.time + Mathf.Max(0.1f, respawnDelay);
            CancelReload();
            serverTriggerHeld = serverSemiQueued = false;
            motor.ServerSetAlive(false);
            motor.ServerSetMovementEnabled(false);
            if (bodyCollider != null) bodyCollider.enabled = false;
            member.MatchManager?.BroadcastKill(killerNetId, netId, cause);
        }

        [Server]
        public void OnServerRespawn(Vector3 position, Quaternion rotation)
        {
            ResetLoadout();
            health = Mathf.Max(1f, maxHealth);
            dead = false;
            reloading = false;
            respawnAt = double.PositiveInfinity;
            serverTriggerHeld = serverSemiQueued = false;
            lastCombatInput = double.NegativeInfinity;
            if (bodyCollider != null) bodyCollider.enabled = true;
        }

        [Server]
        void ResetLoadout()
        {
            EnsureWeaponDefinitions();
            Magazines.Clear();
            Reserves.Clear();
            for (int i = 0; i < WeaponCount; i++)
            {
                var weapon = weapons[i];
                Magazines.Add(Mathf.Max(1, weapon.magazineSize));
                Reserves.Add(Mathf.Max(0, weapon.startingReserve));
                nextFireAt[i] = 0;
            }
            selectedWeapon = 0;
            reloadWeapon = 0;
            reloadEndsAt = 0;
        }

        void EnsureWeaponDefinitions()
        {
            if (weapons == null || weapons.Length != WeaponCount)
                weapons = WeaponSettings.CreatePrototypeDefaults();
            for (int i = 0; i < weapons.Length; i++)
                if (weapons[i] == null) weapons = WeaponSettings.CreatePrototypeDefaults();
        }

        WeaponSettings GetWeapon(int slot)
        {
            return weapons != null && slot >= 0 && slot < weapons.Length ? weapons[slot] : null;
        }

        static int GetAmmo(SyncList<int> list, int slot) => slot >= 0 && slot < list.Count ? list[slot] : 0;
        static bool Finite(Vector3 value) => float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);

        void OnHealthChanged(float previous, float current) => StateChanged?.Invoke();
        void OnSelectedWeaponChanged(byte previous, byte current)
        {
            if (isLocalPlayer) requestedWeapon = current;
            StateChanged?.Invoke();
        }
        void OnReloadChanged(bool previous, bool current) => StateChanged?.Invoke();
        void OnAmmoChanged(SyncList<int>.Operation operation, int index, int previous) => StateChanged?.Invoke();
        void OnDeadChanged(bool previous, bool current)
        {
            ApplyDeathPresentation(current);
            StateChanged?.Invoke();
        }

        void ApplyDeathPresentation(bool isDead)
        {
            if (!isClient) return;
            bool visible = VisibleToLocalRoom();
            if (bodyRenderers != null)
                for (int i = 0; i < bodyRenderers.Length; i++)
                    if (bodyRenderers[i] != null)
                        bodyRenderers[i].enabled = visible && !isDead && !isLocalPlayer && bodyRendererEnabled[i];
            if (visible && isDead)
            {
                if (ragdollPieces.Count == 0) BuildPrimitiveRagdoll();
            }
            else ClearPrimitiveRagdoll();
        }

        bool VisibleToLocalRoom()
        {
            if (!NetworkServer.active) return true;
            var localIdentity = NetworkServer.localConnection != null ? NetworkServer.localConnection.identity : null;
            return localIdentity != null && localIdentity.TryGetComponent<RoomMember>(out var localMember)
                && member != null && member.SharesRoom(localMember);
        }

        void LateUpdate()
        {
            if (isClient) ApplyDeathPresentation(dead);
        }

        void BuildPrimitiveRagdoll()
        {
            AddRagdollPiece(PrimitiveType.Capsule, new Vector3(0, 1.05f, 0), new Vector3(0.55f, 0.65f, 0.4f), Vector3.zero);
            AddRagdollPiece(PrimitiveType.Sphere, new Vector3(0, 1.75f, 0), Vector3.one * 0.34f, Vector3.zero);
            AddRagdollPiece(PrimitiveType.Capsule, new Vector3(-0.42f, 1.15f, 0), new Vector3(0.22f, 0.5f, 0.22f), new Vector3(0, 0, 18));
            AddRagdollPiece(PrimitiveType.Capsule, new Vector3(0.42f, 1.15f, 0), new Vector3(0.22f, 0.5f, 0.22f), new Vector3(0, 0, -18));
            AddRagdollPiece(PrimitiveType.Capsule, new Vector3(-0.2f, 0.45f, 0), new Vector3(0.25f, 0.55f, 0.25f), new Vector3(0, 0, 6));
            AddRagdollPiece(PrimitiveType.Capsule, new Vector3(0.2f, 0.45f, 0), new Vector3(0.25f, 0.55f, 0.25f), new Vector3(0, 0, -6));
        }

        void AddRagdollPiece(PrimitiveType type, Vector3 localPosition, Vector3 localScale, Vector3 localEuler)
        {
            var piece = GameObject.CreatePrimitive(type);
            piece.name = "Ragdoll " + type;
            piece.transform.SetParent(transform, false);
            piece.transform.localPosition = localPosition;
            piece.transform.localRotation = Quaternion.Euler(localEuler);
            piece.transform.localScale = localScale;
            piece.layer = gameObject.layer;
            var rigidbody = piece.AddComponent<Rigidbody>();
            rigidbody.mass = type == PrimitiveType.Sphere ? 0.3f : 0.8f;
            rigidbody.AddForce(UnityEngine.Random.insideUnitSphere * 1.5f, ForceMode.VelocityChange);
            ragdollPieces.Add(piece);
        }

        void ClearPrimitiveRagdoll()
        {
            foreach (var piece in ragdollPieces)
                if (piece != null) Destroy(piece);
            ragdollPieces.Clear();
        }

        public override void OnStopClient() => ClearPrimitiveRagdoll();

        void OnDestroy()
        {
            Magazines.OnChange -= OnAmmoChanged;
            Reserves.OnChange -= OnAmmoChanged;
        }
    }
}
