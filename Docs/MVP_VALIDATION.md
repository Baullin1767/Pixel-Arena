# Pixel Arena MVP validation

Validation date: 2026-09-21 (Europe/Moscow). Unity: 6000.5.10f1. Target: Windows x64 Development build.

## Result

The generated MVP compiles, builds, starts as a dedicated server, accepts independent executable clients, creates isolated rooms, and supports the implemented FPS/combat/bunker loop. Task 07 fixed four defects found during review: late Commands after Leave, a falling dead body, the local FPS body obscuring the camera, and the ricochet gun being an invisible hitscan instead of a visible network projectile.

This document distinguishes executable-process evidence, host runtime checks, automated structural tests and remaining manual coverage. It does not treat compilation as multiplayer proof.

## Build and automated checks

- Final source compilation through MCP: 0 errors.
- Unity Test Framework EditMode assembly `PixelArena.EditModeTests`: 3 passed, 0 failed. It checks player/match/projectile prefab composition, five weapon defaults including rifle 0.12 s and ricochet 10-bounce cap, and Arena structure (8 spawns, one bunker, no scene NetworkIdentity).
- Windows Development build: `Builds/Windows/PixelArena.exe`, 173.63 MB, 0 errors, 8 existing Mirror serialization-analyzer warnings.
- Build scenes: Menu, Arena. SampleScene remains preserved in editor build settings but is not included by the explicit MVP build method.

## Independent process evidence

Logs are in `Logs/Validation`.

- One Windows headless process listened on UDP 7777 and two separate Windows client processes joined successfully.
- Capacity 5 run: both clients reported the same match ID and distinct local net IDs 2 and 3.
- Capacity 1 isolation run: clients reported different match IDs. Server reported two rooms, each with exactly one player and `physics True`:
  - `b9f5e0c7-b872-4ef8-af04-5ec5137397bb`, player netId 2.
  - `8fd5f4d9-dd7c-4bb7-a8e3-8b084f2de0cd`, player netId 4.
- Final isolation logs contain no gameplay error, exception or warning. The expected Mirror startup line and validation lines are in `postfix-server.log`, `postfix-client1.log`, and `postfix-client2.log`.

## Host runtime mechanics

These checks ran in Play Mode with the real NetManager, room loading, NetworkServer and local host connection.

- Host joined `Playing` with player netId 2, HP 100 and a nonempty match ID.
- Server-authoritative movement changed position from `(-19,0,-19)` to approximately `(-16.40,0,-16.35)`; jump produced vertical velocity 7.
- Ladder overlap at the generated east ladder set `IsClimbing=true` and vertical velocity 3.
- All weapon slots fired once and consumed exactly one magazine round: pistol 12→11, rifle 30→29, rocket 1→0, ricochet 20→19, grenade 4→3.
- Two immediate rifle server ticks consumed one round, confirming the 0.12 s cadence gate.
- Pistol reload restored 11→12 and reserve changed 48→47.
- Ricochet fire created a network `CombatProjectile` in Ricochet mode with a visible renderer; its replicated bounce counter is capped by the weapon setting at 10.
- Environment kill was accepted once: HP 0, collider disabled and six primitive ragdoll Rigidbody parts. After 3 seconds the same identity respawned at a spawn with HP 100, collider enabled and no ragdoll parts.
- One bunker countdown ran at the normal 30-second setting. The outside player moved from `(2,0,9)` to a new spawn after detonation, proving death and respawn; detonation count became 1 and cooldown began near 120 seconds. A repeated activation during cooldown was rejected without incrementing the count.
- An accelerated supplementary bunker cycle placed the complete capsule inside the authoritative safe volume. Detonation count became 2 and the player remained alive at `(0,0,17)` with HP 100.
- Leave returned phase None with the transport connected and no local player. Rejoin returned Playing with a new player. After the task-07 fix, the transition emitted zero late movement/combat warnings.

## Review-backed authority and isolation checks

- Movement input is finite-checked, normalized, revision-checked and expires after 0.3 s. The server alone writes Rigidbody velocity and rotation.
- Every authoritative movement, weapon, projectile and bunker query uses the owning Room PhysicsScene and matching RoomMember/MatchId.
- Fire is rejected while dead, stale, reloading or out of ammo; damage cannot supply an arbitrary cross-room source. Duplicate death is guarded by the replicated dead state.
- Room readiness includes the allocated match Guid; stale/wrong room readiness is rejected. Empty rooms destroy network objects and unload their exact additive scene.
- Bunker activation checks playing room, alive/movement state, exact scene/button and distance. Countdown/cooldown use NetworkTime and replicated deadlines.

## Not fully exercised in this environment

- Remote clients were not interactively driven against each other, so cross-process hit registration, kill-feed rendering and visual observation of ten consecutive ricochet bounces remain code-review/host-runtime evidence rather than manual remote-player evidence.
- Concurrent joins during the same scene-load frame, disconnect-before-ready and late join during the bunker countdown/cooldown were not forced with packet-level timing control.
- The full 120-second cooldown was not waited to natural expiry. Its initial deadline, rejection during cooldown and an accelerated return-to-Ready cycle were verified.
- The blast-door closed presentation was not captured during its two-second interval; authoritative phase, detonation and survival/death outcomes were verified.
- LAN/direct-IP operation is implemented. Relay, NAT traversal, authentication, public discovery and cloud hosting are outside this MVP.

## Reproduction

Build through `Pixel Arena > Build Windows MVP`. Start the server with:

```powershell
& '.\Builds\Windows\PixelArena.exe' -batchmode -nographics -logFile 'Logs/server.log'
```

Start clients interactively or with:

```powershell
& '.\Builds\Windows\PixelArena.exe' --client 127.0.0.1 -logFile 'Logs/client.log'
```

For a cheap two-room proof, append `--room-capacity 1 --validation-server` to the server arguments and start two clients.
