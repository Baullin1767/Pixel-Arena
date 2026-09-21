# Acceptance scenarios (not test results)

These scenarios define evidence to collect in task 07. An unchecked scenario is not a pass. Use normal time settings for at least one bunker cycle; accelerated tests may supplement it.

| Scenario | Expected result |
|---|---|
| Fresh main-menu launch | No missing references, network exceptions or automatic unwanted connection |
| Host + remote client Play | Both join same room, each owns only their camera/input, can see the other player |
| Dedicated server + two clients | Same gameplay without host camera or host-only initialization dependencies |
| Fill first room then join another | New room created; no players, projectiles, kills, audio or bunker event leak between rooms |
| Concurrent joins while arena loads | Every accepted connection receives exactly one player; readiness from wrong room is rejected |
| Disconnect before ready / after spawn | Slot freed, player removed, empty room physics scene and objects unloaded |
| Leave then Play again | Clean loading state, fresh membership and exactly one player |
| WASD and mouse, jump and ladder | Movement works on ground and levels; climb only in ladder trigger; pitch clamped; no sprint |
| Fire while dead or reloading | Server rejects it; ammunition and damage remain consistent |
| Pistol held vs clicked | One shot per press; no automatic firing |
| Rifle held | Shots limited to >=0.12 s server cadence |
| Rocket | Server projectile explodes and applies room-only damage once per victim |
| Ricochet gun | Visible bouncing projectiles terminate after at most 10 bounces or lifetime |
| Grenade launcher | Capsule projectile obeys physics and timed/impact explosion policy, room-only damage |
| Reload and weapon switching | Magazine/reserve consistent, cannot bypass reload timing with switching |
| Lethal damage | Exactly one death/kill-feed entry, primitive ragdoll, controls disabled then respawn restored |
| Bunker button E outside reach/dead | Rejected by server |
| Two simultaneous button presses | Exactly one countdown, no timer reset or duplicate siren |
| Bunker 30 s countdown | Door remains traversable until expiry; clients display consistent countdown |
| Detonation with player inside/outside | Inside lives, outside dies once; door closes before kill evaluation |
| Detonation with second room active | Second room unaffected |
| Respawn after detonation | Newly respawned player lives; game continues, door reopens |
| Repeated E during cooldown | Rejected until >=120 s after detonation |
| Join during countdown/cooldown | Correct current bunker phase, door, timer and siren state |
| Invalid input / action spam | No faster movement/fire, nonfinite positions or client-selected damage |
| Build and restart | Standalone launch flow works with exact documented arguments |

Record actual player count/processes, build path, logs, test timestamps and known limitations in MVP_VALIDATION.md. Code review, automated simulated network calls, host-only play, and independent executable clients are different evidence categories; report them separately.
