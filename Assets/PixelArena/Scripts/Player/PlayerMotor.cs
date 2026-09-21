using Mirror;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace PixelArena
{
    [DefaultExecutionOrder(0)]
    [RequireComponent(typeof(RoomMember), typeof(Rigidbody), typeof(CapsuleCollider))]
    public sealed class PlayerMotor : NetworkBehaviour, IPlayerMotorAuthority, IServerRespawnListener
    {
        [Header("Server movement")]
        [Min(0.1f)] public float moveSpeed = 6f;
        [Min(0.1f)] public float jumpSpeed = 7f;
        [Min(0.1f)] public float gravity = 20f;
        [Min(0.1f)] public float terminalSpeed = 40f;
        [Range(0f, 80f)] public float maxGroundAngle = 50f;
        [Min(0.01f)] public float inputTimeout = 0.3f;
        [Header("View and interaction")]
        public float eyeHeight = 1.6f;
        public float mouseSensitivity = 0.12f;
        [Min(0.1f)] public float interactionRange = 3f;
        public LayerMask collisionMask = ~0;
        public LayerMask interactionMask = ~0;
        [SyncVar] bool alive = true;
        [SyncVar] bool movementEnabled = true;
        [SyncVar] bool grounded;
        [SyncVar] bool climbing;

        public struct Pose
        {
            public Vector3 position;
            public float yaw;
            public float pitch;
            public uint revision;
        }
        [SyncVar(hook = nameof(OnPoseChanged))] Pose pose;
        Rigidbody body;
        CapsuleCollider capsule;
        RoomMember member;
        Camera localCamera;
        readonly RaycastHit[] hits = new RaycastHit[32];
        readonly Collider[] overlaps = new Collider[32];
        Vector2 serverMove;
        float serverYaw, serverPitch, localYaw, localPitch;
        double lastInput = double.NegativeInfinity;
        double nextInteraction;
        bool jumpQueued, interactQueued;
        bool localJump, localInteract;
        bool localInputEnabled = true;
        bool botInputActive;
        Vector2 botMove;
        float botYaw, botPitch;
        bool botJump, botInteract;
        float nextSend;
        float ladderDetachUntil;
        uint revision;

        public bool IsAlive => alive;
        public bool MovementEnabled => movementEnabled;
        public bool IsGrounded => grounded;
        public bool IsClimbing => climbing;
        public RoomMember Member => member;
        public Camera LocalCamera => localCamera;
        public bool LocalInputEnabled => isLocalPlayer && localInputEnabled && alive && movementEnabled
            && (botInputActive || (Cursor.lockState == CursorLockMode.Locked && Application.isFocused));
        public Vector3 ServerEyePosition => body.position + Vector3.up * eyeHeight;
        public Vector3 ServerAimDirection => Quaternion.Euler(serverPitch, serverYaw, 0f) * Vector3.forward;
        public Ray ServerAimRay => new Ray(ServerEyePosition, ServerAimDirection);

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            capsule = GetComponent<CapsuleCollider>();
            member = GetComponent<RoomMember>();
            body.useGravity = false;
            body.constraints = RigidbodyConstraints.FreezeRotation;
            body.interpolation = RigidbodyInterpolation.None;
            body.isKinematic = true;
        }
        public override void OnStartServer()
        {
            body.isKinematic = false;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            serverYaw = transform.eulerAngles.y;
            PublishPose();
        }
        public override void OnStartClient()
        {
            if (!isServer) ApplyPose(pose, true);
        }
        public override void OnStartLocalPlayer()
        {
            localYaw = pose.yaw; localPitch = pose.pitch;
            if (!StressTestRuntime.IsBotProcess)
            {
                var cameraObject = new GameObject("Local FPS Camera");
                cameraObject.transform.SetParent(transform, false);
                cameraObject.transform.localPosition = Vector3.up * eyeHeight;
                localCamera = cameraObject.AddComponent<Camera>();
                localCamera.nearClipPlane = 0.05f;
                localCamera.fieldOfView = 75f;
                cameraObject.AddComponent<AudioListener>();
            }
            SetLocalInputEnabled(true);
        }
        public override void OnStopLocalPlayer()
        {
            if (localCamera != null) Destroy(localCamera.gameObject);
            Cursor.lockState = CursorLockMode.None; Cursor.visible = true;
        }
        public void SetLocalInputEnabled(bool enabled)
        {
            if (!isLocalPlayer) return;
            localInputEnabled = enabled;
            Cursor.lockState = enabled ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !enabled;
            localJump = localInteract = false;
        }
        public void SetBotInput(Vector2 move, float yaw, float pitch, bool jump, bool interact)
        {
            if (!isLocalPlayer) return;
            botInputActive = true;
            botMove = Vector2.ClampMagnitude(move, 1f);
            botYaw = Mathf.Repeat(yaw, 360f);
            botPitch = Mathf.Clamp(pitch, -90f, 90f);
            botJump |= jump;
            botInteract |= interact;
        }
        public void ClearBotInput()
        {
            botInputActive = false;
            botMove = Vector2.zero;
            botJump = botInteract = false;
        }
        void Update()
        {
            if (!isLocalPlayer || !NetworkClient.ready) return;
            if (botInputActive)
            {
                localYaw = botYaw;
                localPitch = botPitch;
                if (Time.unscaledTime >= nextSend)
                {
                    nextSend = Time.unscaledTime + 1f / 30f;
                    CmdInput(botMove, localYaw, localPitch, botJump, botInteract, revision);
                    botJump = botInteract = false;
                }
                return;
            }
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                SetLocalInputEnabled(false);
            Vector2 move = Vector2.zero;
            if (LocalInputEnabled)
            {
                var keyboard = Keyboard.current;
                if (keyboard != null)
                {
                    move = new Vector2((keyboard.dKey.isPressed ? 1 : 0) - (keyboard.aKey.isPressed ? 1 : 0),
                        (keyboard.wKey.isPressed ? 1 : 0) - (keyboard.sKey.isPressed ? 1 : 0));
                    localJump |= keyboard.spaceKey.wasPressedThisFrame;
                    localInteract |= keyboard.eKey.wasPressedThisFrame;
                }
                if (Mouse.current != null)
                {
                    Vector2 delta = Mouse.current.delta.ReadValue() * mouseSensitivity;
                    localYaw = Mathf.Repeat(localYaw + delta.x, 360f);
                    localPitch = Mathf.Clamp(localPitch - delta.y, -90f, 90f);
                }
            }
            if (Time.unscaledTime >= nextSend)
            {
                nextSend = Time.unscaledTime + 1f / 30f;
                CmdInput(move, localYaw, localPitch, LocalInputEnabled && localJump, LocalInputEnabled && localInteract, revision);
                localJump = localInteract = false;
            }
        }
        [Command]
        void CmdInput(Vector2 move, float yaw, float pitch, bool jump, bool interact, uint inputRevision)
        {
            if (!CanSimulate() || !alive || !movementEnabled || inputRevision != revision
                || !Finite(move.x) || !Finite(move.y) || !Finite(yaw) || !Finite(pitch)) return;
            serverMove = Vector2.ClampMagnitude(move, 1f);
            serverYaw = Mathf.Repeat(yaw, 360f);
            serverPitch = Mathf.Clamp(pitch, -90f, 90f);
            lastInput = NetworkTime.time;
            jumpQueued |= jump;
            interactQueued |= interact;
        }
        static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        bool CanSimulate() => member != null && member.Room != null && !member.Room.Closing
            && member.Phase == MatchPhase.Playing && member.Room.PhysicsScene.IsValid();
        void FixedUpdate()
        {
            if (!isServer || !CanSimulate()) return;
            if (!alive) { body.linearVelocity = Vector3.zero; return; }
            bool permitted = alive && movementEnabled;
            bool fresh = NetworkTime.time - lastInput <= inputTimeout;
            Vector2 move = permitted && fresh ? serverMove : Vector2.zero;
            bool jump = permitted && fresh && jumpQueued;
            bool interact = permitted && fresh && interactQueued;
            jumpQueued = interactQueued = false;
            grounded = CheckGround();
            LadderVolume ladder = permitted && Time.time >= ladderDetachUntil ? FindLadder() : null;
            climbing = ladder != null;
            Vector3 planar = Quaternion.Euler(0, serverYaw, 0) * new Vector3(move.x, 0, move.y);
            Vector3 velocity = body.linearVelocity;
            velocity.x = planar.x * moveSpeed; velocity.z = planar.z * moveSpeed;
            if (climbing)
            {
                velocity.y = move.y * Mathf.Clamp(ladder.climbSpeed, 0.1f, 10f);
                if (jump)
                {
                    climbing = false; ladderDetachUntil = Time.time + 0.35f;
                    velocity.y = jumpSpeed;
                }
            }
            else
            {
                velocity.y = Mathf.Max(velocity.y - gravity * Time.fixedDeltaTime, -terminalSpeed);
                if (grounded && velocity.y < 0) velocity.y = -2f;
                if (jump && grounded) velocity.y = jumpSpeed;
            }
            body.linearVelocity = velocity;
            body.rotation = Quaternion.Euler(0, serverYaw, 0);
            if (interact && NetworkTime.time >= nextInteraction)
            {
                nextInteraction = NetworkTime.time + 0.2;
                TryServerInteract();
            }
        }
        void CapsulePoints(out Vector3 lower, out Vector3 upper, out float radius)
        {
            radius = capsule.radius;
            Vector3 center = body.position + capsule.center;
            float offset = Mathf.Max(0, capsule.height * 0.5f - radius);
            lower = center - Vector3.up * offset; upper = center + Vector3.up * offset;
        }
        bool CheckGround()
        {
            CapsulePoints(out var lower, out _, out var radius);
            int count = member.Room.PhysicsScene.SphereCast(lower + Vector3.up * 0.04f,
                radius * 0.9f, Vector3.down, hits, 0.16f, collisionMask, QueryTriggerInteraction.Ignore);
            float minNormal = Mathf.Cos(maxGroundAngle * Mathf.Deg2Rad);
            for (int i = 0; i < count; i++)
                if (!hits[i].collider.transform.IsChildOf(transform) && hits[i].normal.y >= minNormal) return true;
            return false;
        }
        LadderVolume FindLadder()
        {
            CapsulePoints(out var lower, out var upper, out var radius);
            int count = member.Room.PhysicsScene.OverlapCapsule(lower, upper, radius, overlaps,
                collisionMask, QueryTriggerInteraction.Collide);
            for (int i = 0; i < count; i++)
                if (overlaps[i].isTrigger && overlaps[i].TryGetComponent<LadderVolume>(out var ladder)
                    && ladder.enabled && ladder.gameObject.scene == gameObject.scene) return ladder;
            return null;
        }
        IPlayerInteractable FindInteraction(PhysicsScene physics, Ray ray)
        {
            int count = physics.Raycast(ray.origin, ray.direction, hits, interactionRange, interactionMask, QueryTriggerInteraction.Ignore);
            RaycastHit nearest = default;
            float distance = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
                if (!hits[i].collider.transform.IsChildOf(transform) && hits[i].distance < distance)
                { nearest = hits[i]; distance = hits[i].distance; }
            if (nearest.collider == null) return null;
            foreach (var component in nearest.collider.GetComponentsInParent<MonoBehaviour>())
                if (component is IPlayerInteractable target) return target;
            return null;
        }
        [Server] public bool TryServerInteract()
        {
            if (!CanSimulate() || !alive || !movementEnabled) return false;
            var target = FindInteraction(member.Room.PhysicsScene, ServerAimRay);
            if (!(target is MonoBehaviour behaviour) || behaviour.gameObject.scene != gameObject.scene
                || !behaviour.isActiveAndEnabled || !target.CanServerInteract(this)) return false;
            target.ServerInteract(this); return true;
        }
        public string GetInteractionPrompt()
        {
            if (!LocalInputEnabled || localCamera == null) return string.Empty;
            PhysicsScene physics = isServer && member.Room != null ? member.Room.PhysicsScene : gameObject.scene.GetPhysicsScene();
            var target = FindInteraction(physics, new Ray(localCamera.transform.position, localCamera.transform.forward));
            return target?.InteractionPrompt ?? string.Empty;
        }
        [Server] public void ServerSetAlive(bool value)
        {
            alive = value; ClearInput();
            if (!value) { climbing = false; grounded = false; }
        }
        [Server] public void ServerSetMovementEnabled(bool value) { movementEnabled = value; ClearInput(); }
        void ClearInput()
        {
            serverMove = Vector2.zero; jumpQueued = interactQueued = false;
            lastInput = double.NegativeInfinity;
            if (!body.isKinematic) body.linearVelocity = Vector3.zero;
        }
        [Server] public void ServerTeleport(Vector3 position, Quaternion rotation)
        {
            ClearInput(); grounded = climbing = false; ladderDetachUntil = 0;
            body.position = position; body.rotation = rotation;
            transform.SetPositionAndRotation(position, rotation);
            serverYaw = rotation.eulerAngles.y; serverPitch = 0; revision++;
            PublishPose();
            if (isLocalPlayer) { localYaw = serverYaw; localPitch = 0; localJump = localInteract = false; }
        }
        [Server] public void OnServerRespawn(Vector3 position, Quaternion rotation)
        {
            alive = true; movementEnabled = true; ServerTeleport(position, rotation);
        }
        void PublishPose()
        {
            pose = new Pose { position = body.position, yaw = serverYaw, pitch = serverPitch, revision = revision };
        }
        void OnPoseChanged(Pose previous, Pose current)
        {
            revision = current.revision;
            if (previous.revision != current.revision)
            {
                if (!isServer) ApplyPose(current, true);
                if (isLocalPlayer) { localYaw = current.yaw; localPitch = current.pitch; localJump = localInteract = false; }
            }
        }
        void ApplyPose(Pose value, bool snap)
        {
            Vector3 position = snap ? value.position : Vector3.Lerp(transform.position, value.position, 1f - Mathf.Exp(-20f * Time.deltaTime));
            transform.SetPositionAndRotation(position, Quaternion.Euler(0, value.yaw, 0));
        }
        void LateUpdate()
        {
            if (isServer) PublishPose();
            else if (isClient) ApplyPose(pose, false);
            if (isLocalPlayer && localCamera != null)
            {
                localCamera.transform.position = transform.position + Vector3.up * eyeHeight;
                localCamera.transform.rotation = Quaternion.Euler(localPitch, localYaw, 0);
            }
        }
    }
}
