using System.Collections.Generic;
using Mirror;
using UnityEngine;

namespace PixelArena
{
    [DefaultExecutionOrder(200)]
    [RequireComponent(typeof(NetworkIdentity), typeof(NetworkMatch), typeof(RoomMember))]
    public sealed class CombatProjectile : NetworkBehaviour
    {
        [SyncVar(hook = nameof(OnModeChanged))] WeaponFireMode mode;
        [SyncVar(hook = nameof(OnPositionChanged))] Vector3 replicatedPosition;
        [SyncVar(hook = nameof(OnRotationChanged))] Quaternion replicatedRotation;

        readonly RaycastHit[] hits = new RaycastHit[64];
        readonly Collider[] overlaps = new Collider[64];
        readonly HashSet<PlayerCombat> explosionTargets = new();
        RoomMember member;
        PlayerCombat owner;
        RoomMember ownerMember;
        Vector3 velocity;
        float damage;
        float gravity;
        float radius;
        float capsuleHalfLength;
        float explosionRadius;
        float maximumTravel;
        float travelled;
        [SyncVar] public int Bounces;
        int maxBounces;
        double expiresAt;
        int layerMask;
        string cause;
        bool initialized;
        GameObject visual;
        Vector3 clientTargetPosition;
        Quaternion clientTargetRotation;

        void Awake() => member = GetComponent<RoomMember>();

        [Server]
        public void ServerInitialize(PlayerCombat source, WeaponSettings settings, Vector3 origin,
            Vector3 direction, LayerMask collisionMask)
        {
            owner = source;
            ownerMember = source != null ? source.Member : null;
            mode = settings.fireMode;
            maxBounces = Mathf.Clamp(settings.maxBounces, 0, 10);
            damage = Mathf.Max(0f, settings.damage);
            gravity = Mathf.Max(0f, settings.projectileGravity);
            radius = Mathf.Max(0.01f, settings.projectileRadius);
            capsuleHalfLength = Mathf.Max(0f, settings.capsuleHalfLength);
            explosionRadius = Mathf.Max(0f, settings.explosionRadius);
            maximumTravel = Mathf.Max(0.1f, settings.range);
            velocity = direction.normalized * Mathf.Max(0.1f, settings.projectileSpeed);
            layerMask = collisionMask;
            cause = settings.displayName;
            transform.SetPositionAndRotation(origin, Quaternion.LookRotation(direction));
            replicatedPosition = origin;
            replicatedRotation = transform.rotation;
            expiresAt = NetworkTime.time + Mathf.Max(2f,
                maximumTravel / Mathf.Max(0.1f, settings.projectileSpeed) + 1f);
            initialized = true;
        }

        public override void OnStartClient()
        {
            clientTargetPosition = replicatedPosition;
            clientTargetRotation = replicatedRotation;
            BuildVisual();
            if (!isServer) transform.SetPositionAndRotation(clientTargetPosition, clientTargetRotation);
        }

        [ServerCallback]
        void FixedUpdate()
        {
            if (!initialized || member == null || member.Room == null || member.Room.Closing
                || !member.Room.PhysicsScene.IsValid() || owner == null || ownerMember == null
                || !member.SharesRoom(ownerMember) || NetworkTime.time >= expiresAt || travelled >= maximumTravel)
            {
                NetworkServer.Destroy(gameObject);
                return;
            }

            float delta = Time.fixedDeltaTime;
            if (mode == WeaponFireMode.Grenade) velocity += Vector3.down * gravity * delta;
            float step = Mathf.Min(velocity.magnitude * delta, maximumTravel - travelled);
            if (step <= 0f)
            {
                Explode(transform.position);
                return;
            }
            Vector3 direction = velocity.normalized;
            if (TryNearestHit(transform.position, direction, step, out var hit))
            {
                if (mode == WeaponFireMode.Ricochet)
                {
                    travelled += hit.distance;
                    var target = hit.collider.GetComponentInParent<PlayerCombat>();
                    if (target != null && target.IsAlive && member.SharesRoom(target.Member))
                    {
                        target.ServerApplyDamage(new DamageInfo(damage, owner.netId, ownerMember, cause, hit.point, direction));
                        NetworkServer.Destroy(gameObject); return;
                    }
                    if (Bounces >= maxBounces) { NetworkServer.Destroy(gameObject); return; }
                    Bounces++;
                    velocity = Vector3.Reflect(velocity, hit.normal);
                    transform.position += direction * hit.distance + hit.normal * 0.015f;
                    transform.rotation = Quaternion.LookRotation(velocity.normalized);
                    replicatedPosition = transform.position; replicatedRotation = transform.rotation;
                    return;
                }
                transform.position = hit.point;
                Explode(hit.point);
                return;
            }
            transform.position += direction * step;
            transform.rotation = Quaternion.LookRotation(direction);
            travelled += step;
            replicatedPosition = transform.position;
            replicatedRotation = transform.rotation;
        }

        bool TryNearestHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            int count;
            if (mode == WeaponFireMode.Grenade && capsuleHalfLength > 0f)
            {
                Vector3 axis = direction * capsuleHalfLength;
                count = member.Room.PhysicsScene.CapsuleCast(origin - axis, origin + axis, radius, direction,
                    hits, distance, layerMask, QueryTriggerInteraction.Ignore);
            }
            else
            {
                count = member.Room.PhysicsScene.SphereCast(origin, radius, direction, hits, distance,
                    layerMask, QueryTriggerInteraction.Ignore);
            }
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                var candidate = hits[i];
                if (candidate.collider == null || (owner != null && candidate.collider.transform.IsChildOf(owner.transform)))
                    continue;
                var combat = candidate.collider.GetComponentInParent<PlayerCombat>();
                if (combat != null && !combat.IsAlive) continue;
                if (candidate.distance < best)
                {
                    best = candidate.distance;
                    nearest = candidate;
                }
            }
            return nearest.collider != null;
        }

        [Server]
        void Explode(Vector3 point)
        {
            if (!initialized) return;
            initialized = false;
            replicatedPosition = point;
            transform.position = point;
            explosionTargets.Clear();
            int count = member.Room.PhysicsScene.OverlapSphere(point, Mathf.Max(radius, explosionRadius), overlaps,
                layerMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                var target = overlaps[i] != null ? overlaps[i].GetComponentInParent<PlayerCombat>() : null;
                if (target == null || !target.IsAlive || target.Member == null || !member.SharesRoom(target.Member)
                    || !explosionTargets.Add(target)) continue;
                Vector3 offset = target.transform.position - point;
                float distance = offset.magnitude;
                float falloff = explosionRadius > 0f ? Mathf.Clamp01(1f - distance / explosionRadius) : 1f;
                float appliedDamage = damage * Mathf.Max(0.25f, falloff);
                target.ServerApplyDamage(new DamageInfo(appliedDamage, owner != null ? owner.netId : 0,
                    ownerMember, cause, point, offset.sqrMagnitude > 0.001f ? offset.normalized : Vector3.up));
            }
            RpcExplosion(point, Mathf.Max(radius, explosionRadius));
            NetworkServer.Destroy(gameObject);
        }

        [ClientRpc]
        void RpcExplosion(Vector3 point, float effectRadius)
        {
            if (!VisibleToLocalRoom()) return;
            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Projectile Explosion";
            flash.transform.position = point;
            flash.transform.localScale = Vector3.one * Mathf.Max(0.2f, effectRadius * 0.35f);
            var collider = flash.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var renderer = flash.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = new Color(1f, 0.35f, 0.05f, 1f);
            Destroy(flash, 0.12f);
        }

        void Update()
        {
            if (!isClient) return;
            if (visual != null) visual.SetActive(VisibleToLocalRoom());
            if (!isServer)
            {
                float blend = 1f - Mathf.Exp(-30f * Time.deltaTime);
                transform.position = Vector3.Lerp(transform.position, clientTargetPosition, blend);
                transform.rotation = Quaternion.Slerp(transform.rotation, clientTargetRotation, blend);
            }
        }

        bool VisibleToLocalRoom()
        {
            if (!NetworkServer.active) return true;
            var localIdentity = NetworkServer.localConnection != null ? NetworkServer.localConnection.identity : null;
            return localIdentity != null && localIdentity.TryGetComponent<RoomMember>(out var localMember)
                && member != null && member.SharesRoom(localMember);
        }

        void OnPositionChanged(Vector3 previous, Vector3 current) => clientTargetPosition = current;
        void OnRotationChanged(Quaternion previous, Quaternion current) => clientTargetRotation = current;
        void OnModeChanged(WeaponFireMode previous, WeaponFireMode current)
        {
            if (isClient) BuildVisual();
        }

        void BuildVisual()
        {
            if (visual != null) Destroy(visual);
            visual = GameObject.CreatePrimitive(mode == WeaponFireMode.Grenade ? PrimitiveType.Capsule : PrimitiveType.Sphere);
            visual.name = mode == WeaponFireMode.Grenade ? "Grenade Visual" : "Rocket Visual";
            visual.transform.SetParent(transform, false);
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = mode == WeaponFireMode.Grenade
                ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
            visual.transform.localScale = mode == WeaponFireMode.Grenade
                ? new Vector3(0.25f, 0.45f, 0.25f) : Vector3.one * 0.3f;
            var collider = visual.GetComponent<Collider>();
            if (collider != null) Destroy(collider);
            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = mode == WeaponFireMode.Grenade
                ? new Color(0.2f, 0.55f, 0.18f, 1f) : new Color(0.95f, 0.2f, 0.08f, 1f);
            visual.SetActive(VisibleToLocalRoom());
        }

        public override void OnStopClient()
        {
            if (visual != null) Destroy(visual);
        }
    }
}
