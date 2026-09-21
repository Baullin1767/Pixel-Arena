# 04 — Arena and bunker contract

Implemented in `Assets/PixelArena/Scripts/World`: `ArenaBuilder`, `ArenaBunker`, `BunkerButton`, `BunkerController`. Existing networking/player/combat files and saved scenes were not modified.

## Integration (task 06)

1. Add `PixelArena.BunkerController` to the **registered MatchManager prefab**, alongside its existing NetworkIdentity, NetworkMatch and MatchManager. Do this before spawning; server/client prefab composition must match. No separate bunker prefab or network spawn registration is needed.
2. Create an empty arena scene and invoke `PixelArena.ArenaBuilder.Build(scene, structureMaterial, accentMaterial)` through the editor builder/MCP, then save as `Assets/PixelArena/Scenes/Arena.unity`. Both material arguments are optional; pass persistent material assets for a colored saved arena. Build refuses a scene already containing an ArenaBunker. It does not clear other objects, save scenes, or modify build settings.
3. Builder creates a 48 × 48 arena, perimeter walls, two 4 m platforms, bridge, staircase, vertical trigger ladder, cover/passages, eight ArenaSpawnPoint markers, directional light, and bunker. No cameras/listeners or NetworkIdentity objects are created in static geometry. PlayerMotor supplies the local camera.
4. Preserve generated `ArenaBunker` references to safeVolume, door and button. Door position is local to the bunker root. Keep the safe volume a trigger. Put BunkerButton on its solid raycastable collider; the motor handles E raycast, nearest-hit/line-of-sight and input rate validation.

The server controller locates its marker using the **exact Room.Scene**, not a scene name or global object search. Remote clients bind their one loaded arena; host controllers bind only their own server scenes. All generated content exists when the saved scene loads, as required by the static visibility capture in NetManager. Build is an edit-time generation method, not a runtime scene bootstrap.

## Bunker behavior

- `Ready`: door open; activation requires a living movement-enabled player in the same playing room/scene, the exact assigned button, and authoritative eye distance <= 3 m. The motor independently validates its own interaction range and obstruction.
- `Countdown`: siren runs for that client's own match; default duration 30 seconds, using NetworkTime. Further activation is rejected.
- At the deadline: phase changes to Cooldown **before damage**, door moves closed with collider enabled, and the server traverses only this room's current players. Each living outside player receives `PlayerCombat.ServerKillEnvironment("Bunker")` once. Combat supplies its own duplicate-death protection and room-only kill feed.
- Protection requires the complete authoritative CapsuleCollider bounds inside the marker volume, not merely the feet or a client trigger claim. Partial doorway bodies, bodies above the roof and different-scene players are not protected. The conservative bounds check also works with transformed volume coordinates.
- Door reopens after editable `doorHoldSeconds` (default 2 seconds). It remains collidable at its raised/open position. Cooldown defaults to **120 seconds after actual detonation**, not after reopening. Ready returns at cooldown expiry. Respawns are not affected by the completed blast.
- All timings are editable prototype fields on BunkerController. Network state carries phase, deadlines and detonation counter, so joining clients receive the current state without replaying old explosions. Countdown siren is a generated audio clip on the room-scoped network object and explicitly gates on local room membership; dedicated servers never play it.

## HUD surface (task 05)

`BunkerController.Local` returns the controller matching NetworkClient.localPlayer's nonempty RoomMember.MatchId, or null. Read:

- `Phase : BunkerPhase` (Ready / Countdown / Cooldown)
- `PhaseEndsAt : double`, `RemainingSeconds : double`
- `DoorsOpenAt : double`, `DoorsClosed : bool`
- `DetonationCount : uint`
- `StateChanged : event Action` (phase/deadline/door/detonation changes; poll RemainingSeconds for the countdown display)

`BunkerButton.InteractionPrompt` supplies the motor's E prompt. UI should use Phase to present ready/countdown/cooldown labels. Fields use Mirror SyncVars; only server methods mutate the authoritative state. No client activation command is added to the bunker.

## Checks performed and remaining acceptance

- Scripts authored/imported via MCP for Unity; live execution resolved ArenaBuilder and BunkerController successfully.
- Temporary additive scene built inside Unity; assertions passed for eight spawns, no static network identities, ladder trigger, safe full interior body, unsafe partially entered doorway body, different-scene rejection, enabled door collider, exact closed/open transforms, and duplicate build rejection.
- Generated geometry inspected with a positioned screenshot. Temporary test/preview scenes closed afterward; SampleScene remains active and was not saved/modified by this task.
- Final console check: zero errors/warnings.
- No real connected host/client session was run: registered MatchManager/player/projectile prefabs and saved arena are assembled by task 06. Tasks 06/07 must verify movement over stairs/ladder and through doorway, 30-second countdown, one kill per outside player, inside survival, 2-second door hold, 120-second cooldown, respawn survival, late joins in each phase, and capacity-one two-room isolation of kills/doors/siren. Compilation and geometry checks are not full multiplayer acceptance.
