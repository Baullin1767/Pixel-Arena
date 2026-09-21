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
        Vector3 previousPosition;
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
        Rigidbody grenadeBody;
        CapsuleCollider grenadeCollider;
        PhysicsMaterial grenadeMaterial;
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
            previousPosition = origin;
            replicatedPosition = origin;
            replicatedRotation = transform.rotation;
            expiresAt = NetworkTime.time + Mathf.Max(2f,
                maximumTravel / Mathf.Max(0.1f, settings.projectileSpeed) + 1f);
            if (mode == WeaponFireMode.Grenade) ConfigureGrenadePhysics(settings, direction.normalized);
            initialized = true;
        }

        [Server]
        void ConfigureGrenadePhysics(WeaponSettings settings, Vector3 direction)
        {
            grenadeMaterial = new PhysicsMaterial("Grenade Bounce")
            {
                bounciness = Mathf.Clamp01(settings.projectileBounciness),
                dynamicFriction = Mathf.Clamp01(settings.projectileFriction),
                staticFriction = Mathf.Clamp01(settings.projectileFriction),
                bounceCombine = PhysicsMaterialCombine.Maximum,
                frictionCombine = PhysicsMaterialCombine.Minimum
            };

            grenadeCollider = gameObject.AddComponent<CapsuleCollider>();
            grenadeCollider.direction = 2;
            grenadeCollider.radius = radius;
            grenadeCollider.height = Mathf.Max(radius * 2f, (capsuleHalfLength + radius) * 2f);
            grenadeCollider.sharedMaterial = grenadeMaterial;

            grenadeBody = gameObject.AddComponent<Rigidbody>();
            grenadeBody.mass = Mathf.Max(0.01f, settings.projectileMass);
            grenadeBody.useGravity = false;
            grenadeBody.interpolation = RigidbodyInterpolation.None;
            grenadeBody.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            grenadeBody.linearDamping = 0.02f;
            grenadeBody.angularDamping = 0.05f;
            grenadeBody.linearVelocity = direction * Mathf.Max(0.1f, settings.projectileSpeed);
            grenadeBody.angularVelocity = new Vector3(8f, 5f, 3f);

            if (owner != null)
            {
                foreach (var ownerCollider in owner.GetComponentsInChildren<Collider>())
                    if (ownerCollider != null) Physics.IgnoreCollision(grenadeCollider, ownerCollider);
            }
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
                || !member.SharesRoom(ownerMember))
            {
                NetworkServer.Destroy(gameObject);
                return;
            }

            if (NetworkTime.time >= expiresAt || travelled >= maximumTravel)
            {
                if (mode == WeaponFireMode.Grenade) Explode(transform.position);
                else NetworkServer.Destroy(gameObject);
                return;
            }

            if (mode == WeaponFireMode.Grenade)
            {
                UpdatePhysicalGrenade();
                return;
            }

            float delta = Time.fixedDeltaTime;
            float step = Mathf.Min(velocity.magnitude * delta, maximumTravel - travelled);
            if (step <= 0f)
            {
                Explode(transform.position);
                return;
            }
            Vector3 direction = velocity.normalized;
            Vector3 currentPosition = previousPosition + direction * step;
            if (TryNearestHit(previousPosition, direction, Vector3.Distance(previousPosition, currentPosition), out var hit))
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
                    transform.position = hit.point + hit.normal * 0.015f;
                    previousPosition = transform.position;
                    transform.rotation = Quaternion.LookRotation(velocity.normalized);
                    replicatedPosition = transform.position; replicatedRotation = transform.rotation;
                    return;
                }
                transform.position = hit.point;
                Explode(hit.point);
                return;
            }
            transform.position = currentPosition;
            previousPosition = currentPosition;
            transform.rotation = Quaternion.LookRotation(direction);
            travelled += step;
            replicatedPosition = transform.position;
            replicatedRotation = transform.rotation;
        }

        [Server]
        void UpdatePhysicalGrenade()
        {
            if (grenadeBody == null || grenadeCollider == null)
            {
                NetworkServer.Destroy(gameObject);
                return;
            }

            Vector3 currentPosition = grenadeBody.position;
            travelled += Vector3.Distance(previousPosition, currentPosition);
            previousPosition = currentPosition;
            grenadeBody.AddForce(Vector3.down * gravity, ForceMode.Acceleration);
            replicatedPosition = currentPosition;
            replicatedRotation = grenadeBody.rotation;
        }

        bool TryNearestHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit nearest)
        {
            nearest = default;
            int count = member.Room.PhysicsScene.Raycast(origin, direction, hits, distance,
                layerMask, QueryTriggerInteraction.Ignore);
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

        [ServerCallback]
        void OnCollisionEnter(Collision collision)
        {
            if (!initialized || mode != WeaponFireMode.Grenade || collision.collider == null) return;
            if (owner != null && collision.collider.transform.IsChildOf(owner.transform)) return;
            Bounces++;
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
            float visualRadius = Mathf.Max(0.5f, effectRadius);
            var effect = new GameObject("Projectile Explosion Effect");
            effect.transform.position = point;

            var particles = effect.AddComponent<ParticleSystem>();
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = particles.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.6f;
            main.maxParticles = 48;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.55f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(visualRadius * 0.8f, visualRadius * 2.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, Mathf.Max(0.12f, visualRadius * 0.09f));
            main.startColor = new ParticleSystem.MinMaxGradient(
                new Color(1f, 0.85f, 0.2f, 1f), new Color(1f, 0.12f, 0.01f, 1f));
            main.gravityModifier = 0.65f;

            var emission = particles.emission;
            emission.enabled = false;
            var shape = particles.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.05f, visualRadius * 0.08f);
            particles.Play();
            particles.Emit(Mathf.Clamp(Mathf.RoundToInt(20f + visualRadius * 6f), 20, 48));

            var explosionLight = effect.AddComponent<Light>();
            explosionLight.type = LightType.Point;
            explosionLight.color = new Color(1f, 0.32f, 0.04f);
            explosionLight.range = Mathf.Max(2f, visualRadius * 1.75f);
            explosionLight.intensity = Mathf.Max(2f, visualRadius * 1.5f);
            explosionLight.shadows = LightShadows.None;
            Destroy(explosionLight, 0.14f);

            var flash = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            flash.name = "Explosion Flash";
            flash.transform.SetParent(effect.transform, false);
            flash.transform.localScale = Vector3.one * Mathf.Max(0.2f, visualRadius * 0.35f);
            var collider = flash.GetComponent<Collider>();
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            var renderer = flash.GetComponent<Renderer>();
            if (renderer != null)
            {
                var flashMaterial = renderer.material;
                flashMaterial.color = new Color(1f, 0.35f, 0.05f, 1f);
                Destroy(flashMaterial, 0.2f);
            }
            Destroy(flash, 0.12f);
            Destroy(effect, 0.9f);
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
            if (collider != null)
            {
                collider.enabled = false;
                Destroy(collider);
            }
            var renderer = visual.GetComponent<Renderer>();
            if (renderer != null) renderer.material.color = mode == WeaponFireMode.Grenade
                ? new Color(0.2f, 0.55f, 0.18f, 1f) : new Color(0.95f, 0.2f, 0.08f, 1f);
            visual.SetActive(VisibleToLocalRoom());
        }

        public override void OnStopClient()
        {
            if (visual != null) Destroy(visual);
        }

        void OnDestroy()
        {
            if (grenadeMaterial != null) Destroy(grenadeMaterial);
        }
    }
}
