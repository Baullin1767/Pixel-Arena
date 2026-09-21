# 01 — SimpleMatch networking contract

Implemented in existing global SimpleMatch classes plus `PixelArena` networking components. Mirror was preserved. No scenes or prefabs were created or modified by this task.

## Required integration (task 06)

- Persistent menu root: `NetManager`, a configured Mirror transport, **one** `PixelArena.RoomInterestManagement`. NetManager requires that component; do not add another AOI. Leave Mirror online/offline scene fields empty. `autoCreatePlayer` is forced false.
- `NetManager.arenaScene` defaults to `Assets/PixelArena/Scenes/Arena.unity`; add that scene to build settings. Capacity defaults to 5; set 1 to test two-room isolation cheaply.
- Assign `NetManager.matchManager` to a registered prefab containing NetworkIdentity, NetworkMatch and MatchManager. Add this prefab to NetworkManager.spawnPrefabs.
- Assign NetworkManager.playerPrefab to the player prefab with NetworkIdentity, NetworkMatch and RoomMember plus later motor/combat components. MatchManager optionally overrides its playerPrefab. All network behaviour composition must match between server/client; nothing is dynamically added after spawn.
- Arena scene contains geometry, local markers/scripts and spawn points, **no scene NetworkIdentity objects**. Use `ArenaSpawnPoint` markers, or children of a root named `SpawnPoints`. Missing spawn points fail the room instead of placing players at unsafe defaults.
- Networked world objects/projectiles use registered prefabs instantiated on the server, `room.matchManager.PrepareRoomObject(obj)`, then `NetworkServer.Spawn(obj)`. Their root needs NetworkMatch; RoomMember is useful for authoritative association. Never use Visibility.ForceShown for room objects.

## Actual interfaces for tasks 02–05

- `RoomMember.MatchId : Guid` and `Phase : MatchPhase` are Mirror SyncVars (not Unity serialized). `RoomMember.Room` and `.MatchManager` are **server-only associations**; clients use MatchId and replicated gameplay state. `SharesRoom(other)` compares nonempty IDs.
- `MatchPhase`: None, Loading, Playing, Leaving, Error. This is the matchmaking lifecycle; bunker/combat maintain their own replicated phases.
- `Room.Scene` is the exact server scene handle; `Room.PhysicsScene` is its isolated local physics world. Never resolve a server room by scene name: several loaded scenes have the same name/path.
- `Room.Players`, `PlayerCount`, `maxPlayers`, `roomId`, `matchManager`; `NetManager.matchService.FindRoomByConnection(conn)` / `FindRoomById(id)` / `Rooms` for server lookup.
- `MatchManager.Room`, replicated `MatchId` / `Phase`, `RoomPhysics`.
- `[Server] MatchManager.TryGetSpawnPoint(out Vector3 position, out Quaternion rotation)` returns false when unconfigured, otherwise round-robin marker poses.
- `[Server] MatchManager.PrepareRoomObject(GameObject obj)` moves a root object into the correct scene and assigns NetworkMatch and optional RoomMember **before spawn**.
- `[Server] MatchManager.ServerRespawn(NetworkIdentity player)` checks room membership, moves the existing identity to a room spawn and calls each root component implementing `PixelArena.IServerRespawnListener.OnServerRespawn(Vector3, Quaternion)`. Motor implements authoritative velocity/ground state reset and teleport replication; combat resets HP/death separately. No replacement identity on respawn.
- `MatchManager.ServerPlayerSpawned : event Action<NetworkIdentity>` after initial AddPlayer. A motor can also initialize in OnStartServer: RoomMember has already been assigned.
- `MatchManager.BroadcastKill(uint killerNetId, uint victimNetId, string cause)` sends only to ready room members. Call only after server-confirmed kill; zero killer ID can represent bunker/environment.
- `NetManager.ClientMatchId`, `ClientPhase`, `StatusText`; event `ClientStatusChanged(MatchPhase, string)`; event `KillFeedReceived(KillFeedMessage)` filtered against current room.
- UI invokes inherited `StartHost`, `StartServer`, `StartClient`, connection address and stop methods; then `ClickJoinRoom()` / `ClickLeaveRoom()`. Leaving preserves the connection; Play can join again after None/Error status.

## Physics and movement contract

Every server room is a separately loaded additive scene with LocalPhysicsMode.Physics3D. MatchManager simulates its room once each FixedUpdate at execution order 1000. Motors and projectiles should run before 1000; do not also simulate the scene. All authoritative raycasts, sphere/capsule casts, overlap queries and ground checks use Room.PhysicsScene/MatchManager.RoomPhysics, never global Physics queries. Move all authoritative physics bodies into the room before use. Authoritative input must still be validated by later tasks; this layer does not implement movement/damage.

Ordinary remote clients load one arena geometry scene into their default physics scene and reuse it across room changes. They never load every server room. Host loads no separate client arena and uses its server room geometry. Client-side camera/visual queries therefore need an explicit host path if using physics.

## Readiness, lifetime and visibility

Join reserves one room slot; duplicate join requests do not allocate another slot. Server scene loads are serialized to reliably capture each actual Scene instance. Readiness message carries the allocated Guid; stale/wrong Guid is ignored. Ready may arrive before the server scene finishes; it is saved until initialization. Generic Mirror Ready/AddPlayer cannot bypass the custom handshake. Player creation is idempotent.

RoomInterestManagement subclasses the installed MatchInterestManagement, adding its missing null guard for a connection without identity. Match AOI enforces equal nonempty NetworkMatch IDs. Mirror's installed host visibility method also toggles networked renderers, lights, audio, terrain and particles.

For **static arena geometry**, NetManager captures original Renderer/Light/AudioSource/Camera/AudioListener enabled state in sceneLoaded and initially disables presentation. Its LateUpdate changes presentation only when the room becomes visible/hidden, preserving the base NetworkManager.LateUpdate. Hiding saves the current enabled values; showing restores them. It does not overwrite desired state each frame. Colliders remain active in their isolated PhysicsScene. Static scene content must exist by sceneLoaded. Use primitive mesh geometry (static Terrain rendering is not managed here). Do not create arena cameras/listeners that compete with the local player camera.

Dynamic bunker siren/lights belong to a spawned NetworkIdentity with NetworkMatch, not static presentation capture. Their client presentation must gate on local room membership/visibility as well as bunker state, especially on host; server gameplay must never turn on hidden-room audio. Change gameplay state independently from renderer/audio enabled state. This avoids a static visibility gate restoring an obsolete siren state or revealing another room's dynamic effects.

Leave/disconnect sets the connection not ready, removes observers, destroys its player and frees the slot. The last departure destroys room network objects and unloads its exact scene. Closing during scene loading unloads that scene as soon as loading completes. StopServer closes all rooms; disconnect invalidates pending client loads and unloads client geometry. Generation counters and AsyncOperation.completed cleanup handle loads that finish after stop/reset even if their coroutine was interrupted; subsequent loads wait for outstanding operations and old coroutines cannot consume the new server queue. Loading flags reset on stop/reset. In-flight readiness is checked against membership and current Guid. Scene/prefab failures return an error and release the room.

## Checks and remaining validation

- Authored/imported scripts and compiled through MCP, explicitly targeting `Pixel Arena@1e4793473d861000`.
- Final compilation completed; MCP console reports zero errors and warnings.
- Executed assertions inside Unity: capacity 1 rejects a second connection and duplicate member; leave frees capacity; rejoin succeeds; Clear empties membership; room ID is nonempty. All passed.
- No real host/client gameplay test yet: arena, registered prefabs, motor and UI are produced by later tasks. Task 06/07 must verify spawn synchronization, stop/restart, leave while loading, late join and room-capacity-1 isolation on host plus remote clients. This task makes no claim of end-to-end acceptance.
- Room reservation currently has no readiness timeout; a connected client that never acknowledges loading retains its slot until leave/disconnect. LAN/direct-address networking only; no relay/NAT traversal or public hosting service is introduced.
