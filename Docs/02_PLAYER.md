# 02 — FPS player controller contract

Implemented under `Assets/PixelArena/Scripts/Player`. No scene or prefab asset was created or modified by this task; task 06 wires the player prefab.

## Player prefab setup (task 06)

The player root must contain this exact gameplay composition:

- `NetworkIdentity`, `NetworkMatch`, `RoomMember` (networking contract from task 01).
- `PlayerMotor`.
- `Rigidbody`. `PlayerMotor.Awake` disables built-in gravity, freezes rotation, makes client bodies kinematic, and uses `ContinuousDynamic` on the server. Do not enable client authority or add a client-authoritative `NetworkTransform`; `PlayerMotor` replicates the authoritative pose itself.
- `CapsuleCollider`. Recommended primitive values are height `2`, radius `0.4`, center `(0, 1, 0)`, with the root pivot at the feet. Keep `PlayerMotor.eyeHeight` inside the capsule; default is `1.6`.
- Later combat/presentation components on the same root so `MatchManager.ServerRespawn` discovers their `IServerRespawnListener` implementations.

The prefab must be assigned as described in `Docs/01_NETWORK.md`. `MatchManager.PrepareRoomObject` moves the instantiated root into the room scene before `NetworkServer.AddPlayerForConnection`; this is required so its Rigidbody belongs to the room's isolated `PhysicsScene`.

## Input, movement and replication

`PlayerMotor` reads the Input System devices directly for the local player only: WASD, mouse delta, Space and E. Mouse pitch is clamped to `-90..90`; yaw wraps. There is no sprint. Escape releases the cursor, and UI can call `SetLocalInputEnabled(bool)` to acquire/release gameplay input. A runtime camera and audio listener are created only by `OnStartLocalPlayer` and destroyed by `OnStopLocalPlayer`; remote players never receive a camera.

The owning client sends normalized movement, view and edge-triggered jump/interact intent at 30 Hz. The server rejects non-finite values, clamps movement and pitch, ignores input after `inputTimeout` (default `0.3 s`), and rejects packets from before the latest teleport revision. The server alone writes Rigidbody velocity/rotation. `PlayerMotor` has execution order `0`; `MatchManager` simulates the isolated room physics at order `1000`.

Ground checks, ladder overlap checks and authoritative E raycasts use `RoomMember.Room.PhysicsScene`, never global `Physics`. Server movement is allowed only while the room exists, is not closing and is in `MatchPhase.Playing`. Authoritative position/yaw/pitch are replicated in `PlayerMotor.Pose`; non-server clients interpolate position and apply server yaw. Local view rotation remains responsive while the server owns the body pose.

## Ladder contract

Add `LadderVolume` to a GameObject with a `BoxCollider`. The component forces the collider to be a trigger. Set `climbSpeed` as needed (default `3`). While the authoritative player capsule overlaps that trigger in the same room scene, forward/back input controls vertical velocity. Space jumps away and suppresses immediate reattachment for `0.35 s`.

The ladder must be included by `PlayerMotor.collisionMask`. It is static room geometry; it needs no `NetworkIdentity`.

## Interaction contract

An E target implements `PixelArena.IPlayerInteractable` on the hit collider or one of its parents:

```csharp
string InteractionPrompt { get; }
bool CanServerInteract(PlayerMotor player);
void ServerInteract(PlayerMotor player);
```

The motor first validates that the player is alive, movement-enabled and in a playing room, then raycasts from the authoritative server eye pose in that room's `PhysicsScene`, chooses the nearest non-self hit within `interactionRange` (default `3`), requires the target component to be active and in the same scene, and rate-limits attempts to one per `0.2 s`. It then calls `CanServerInteract`; implementations must validate their own state/cooldown before mutating it. `GetInteractionPrompt()` performs only a local presentation query for HUD text and is not authority.

## Combat/death API

Combat can depend on `PixelArena.IPlayerMotorAuthority`, implemented by `PlayerMotor`:

- State: `IsAlive`, `MovementEnabled`, `Member`.
- Authoritative aim: `ServerEyePosition`, `ServerAimDirection`, `ServerAimRay`.
- Server mutations: `ServerSetAlive(bool)`, `ServerSetMovementEnabled(bool)`, `ServerTeleport(Vector3, Quaternion)`.

All mutation methods are `[Server]` on `PlayerMotor`. Death should call `ServerSetAlive(false)`; this clears input and velocity and blocks movement, jump, climbing and E. Combat controls its own HP, ragdoll and respawn delay. At respawn it calls `MatchManager.ServerRespawn(identity)`. The manager places the existing identity at a room spawn and invokes `OnServerRespawn`; the motor restores alive/movement state, clears velocity/ground/ladder/input, resets pitch, advances the teleport revision and publishes an immediate authoritative pose. Combat should implement its own `IServerRespawnListener` to reset HP/presentation separately.

For weapon hit tests and projectiles, obtain the isolated physics scene through `motor.Member.MatchManager.RoomPhysics` or `motor.Member.Room.PhysicsScene` on the server. Client-provided aim is clamped but remains an intent; damage, hit selection, ammo and cadence still require server validation in task 03.

## Checks and limitations

- Authored/imported through MCP for Unity against `Pixel Arena@1e4793473d861000`.
- Unity script compilation completed and the Console reported zero errors and warnings.
- MCP script validation reported zero errors for all three scripts. Its two generic warnings (string concatenation in `Update`, null-checking `GetComponent`) do not correspond to a string concatenation in the motor and the ladder collider is guaranteed by `RequireComponent`.
- No player prefab or Arena scene exists yet, so connected runtime movement, camera, ladder and E behavior cannot be exercised until task 06 wires the assets. End-to-end multiplayer validation remains task 07.
- Movement is server-authoritative without client-side prediction/reconciliation. Remote/local body motion is interpolated from replicated poses; high-latency play will feel delayed, which is acceptable for this LAN MVP.
- The runtime local camera uses default camera settings apart from FOV `75` and near clip `0.05`; task 06/05 must ensure the scene has no competing camera/audio listener and UI releases/restores the cursor through `SetLocalInputEnabled`.
