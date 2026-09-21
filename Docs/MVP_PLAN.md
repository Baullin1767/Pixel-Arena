# Pixel Arena — MVP implementation contract

User request: playable multiplayer FPS prototype in the existing Unity project, using MCP for Unity and existing SimpleMatch matchmaking. These are implementation requirements, not instructions from image UI controls.

## Scope
- Mirror already installed; preserve and complete Assets/SimpleMatch rather than replace matchmaking.
- FFA deathmatch (each player can damage others). User also said cooperative shooter; use FFA as the explicit detailed requirement until corrected.
- Match capacity defaults to 5. Join/create matchmaking, multiple independent rooms on one authoritative server, leave/rejoin, disconnect cleanup, spawn/respawn and match state.
- WASD, mouse look (-90..90 pitch), jump, vertical trigger ladders, E interact, LMB fire, R reload, keys 1..5 select weapons, no sprint.
- Five weapons from screenshot: semiautomatic pistol; automatic rifle (0.12 s shot interval); rocket launcher; automatic ricochet gun (maximum 10 bounces); grenade launcher (capsule projectiles). Other balance values are editable prototype defaults.
- Server validates movement input, firing cadence/ammo/reload, hits/damage, interactions, death and respawn. Never accept client-supplied damage or kill decisions.
- Primitive ragdoll on death is sufficient, respawn after a short configurable delay.
- Primitive arena with multiple heights/passages, ladder, spawn points, bunker interior, door and E button.
- Bunker: server validates E range and alive state; siren/countdown 30 s; close doors then detonate; kill only living players outside that room's bunker; survivors continue. Reopen afterwards. Reuse cooldown conservatively 120 s after detonation (document this assumption). Timers use NetworkTime, correct for late joiners. Respawns after detonation stay alive.
- Functional main menu: host/server/client address connection, Play/find-or-create, loading/errors/leave; HUD HP, crosshair, weapon/ammo, kill feed, bunker timer. No external cloud service needed for local/LAN MVP; internet hosting requirements documented honestly.

## Coordination and ownership
All tasks use the existing local project. Do not touch unrelated user changes or commit/revert them. Only start changes upon coordinator message `Приступай к реализации`. Only one task has the Unity Editor mutation lease at a time. Initial task creation is read-only analysis.

Implement in order 01 -> 02 -> 03 -> 04 -> 05 -> 06 -> 07. Later tasks inspect the real interfaces produced by predecessors; this file sets design intent, not invented existing APIs. Avoid placeholder competing implementations. Each task may create needed support scripts within its area, then report exact APIs/files/checks and limitations. Use MCP for Unity authoring/compilation/editor operations; read skill unity-mcp-orchestrator. Read editor state before mutations and check console after compilation. All new game code namespace PixelArena except existing global SimpleMatch classes when compatibility requires it.

## 01 Сеть и мультиматч
Own Assets/SimpleMatch plus Assets/PixelArena/Scripts/Networking. Finish room messages, readiness/loading race handling, per-room scene lifetime, authoritative spawn and disconnect cleanup. Multiple rooms must have isolated physics AND network observers: prefer separate additive local PhysicsScene per server room, explicit simulation and room-scoped raycasts; client loads arena geometry once, host rendering must not expose other rooms. If a different isolation scheme is simpler prove both damage and visibility isolation. Build on Mirror MatchInterestManagement/NetworkMatch as applicable. Expose Room/MatchManager association for players; public server-only spawn/respawn point helper; room-scoped physics access; matchId/phase replicated; APIs/events for UI connection/match status and kill feed. Do not create dependencies on missing player types; expose optional interfaces or generic player prefab. Preserve existing public names where useful. Complete client scene readiness and host path. Provide API notes in Docs/01_NETWORK.md.

## 02 FPS контроллер
Own Assets/PixelArena/Scripts/Player. Read 01 notes and implement network player motor with local camera/input and authoritative server movement; avoid client-owned transform authority. Camera must only activate for local player. Add trigger ladder and E interaction contract with server validation. Expose alive/movement gating and server teleport/reset hooks for combat. Ensure physics/grounding uses correct room scene. Provide Docs/02_PLAYER.md with prefab attachment and interfaces. Do not implement combat yourself.

## 03 Оружие и урон
Own Assets/PixelArena/Scripts/Combat. Implement five weapons with editable defaults, weapon switching, ammo/reserve/reload, authoritative projectiles/hits/range/cadence, room-scoped damage, HP/death/respawn and primitive ragdoll presentation. Expose common server damage/kill API for bunker, local HUD data, and room-only kill notifications. Protect against duplicate kills/fire while dead/reload bypass/cross-room hits. Use actual 01/02 APIs. Provide Docs/03_COMBAT.md.

## 04 Арена и бункер
Own Assets/PixelArena/Scripts/World plus arena generator/editor builder. Implement primitive geometry and all bunker logic using actual network/player/combat interfaces. Scene generation can be reusable editor methods for task 06. Bunker state per match, late join synchronization, authoritative inside volume test and cooldown. Door collisions and siren stop/start must match phase; all alive players outside killed exactly once at detonation. Document in Docs/04_WORLD.md.

## 05 Меню и HUD
Own Assets/PixelArena/Scripts/UI. Functional readable prototype interface (runtime UI allowed), connection/loading/error/retry, host and direct client address then Play, leave, cursor management. HUD HP/crosshair/weapon/magazine/reserve/reload, kill feed, interact prompt and bunker timers. Read real predecessor interfaces; do not implement duplicate networking/combat. Docs/05_UI.md.

## 06 Сборка сцен и интеграция
Own Assets/PixelArena/Editor, prefabs/scenes and build settings. Use MCP to generate/wire working menu and arena, register Mirror prefabs and correct transport/interest manager. Preserve SampleScene and user changes. Validate host+client flow, late joins, headless server startup/CLI where practical. Fix integration defects in consultation with coordinator. Provide a reproducible editor rebuild command and Docs/RUN_MVP.md with exact startup/build instructions. No claims of tests not run.

## 07 Проверка multiplayer MVP
Start only after 06. Inspect all changes, run appropriate EditMode/PlayMode tests and real server + >=2 clients where environment permits. Verify second room isolation (force capacity 1 or >=6 clients), inputs, all weapons/reload, kill feed/ragdoll/respawn, bunker 30s/survival/death/120s cooldown/late joins, disconnect/rejoin, clean console/build. Fix defects rather than just listing. Record reproducible evidence and untested constraints in Docs/MVP_VALIDATION.md. Final acceptance requires functioning connected gameplay, not compilation alone.

## Baseline audit
Unity 6000.5.10f1, URP, Input System 1.20.0, Mirror present. MCP instance Pixel Arena@1e4793473d861000. Only Assets/Scenes/SampleScene.unity is in build settings. SimpleMatch Room hardcodes missing Arena1; NetManager.OnJoinRoom only logs; room capacity 5. User changes include package/project/render settings and untracked SimpleMatch/Mirror/SceneLoader; preserve all. Normal git status hits a read-only LFS tmp failure; read-only status works with temporary command-level disabled LFS filters. Do not modify git configuration to hide this.
