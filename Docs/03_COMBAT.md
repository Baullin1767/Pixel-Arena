# 03 — Weapon and damage contract

Implemented under `Assets/PixelArena/Scripts/Combat`. No prefab or scene asset was changed by this task; task 06 wires the components and registered projectile prefab.

## Player prefab setup (task 06)

Add `PlayerCombat` to the same root as `NetworkIdentity`, `NetworkMatch`, `RoomMember`, `PlayerMotor`, `Rigidbody` and `CapsuleCollider`. `PlayerCombat` consumes the authoritative aim and alive state from `PlayerMotor`; it does not add client transform authority. The five editable prototype weapon definitions are created automatically in this order: pistol, automatic rifle (0.12 second interval), rocket launcher, automatic ricochet gun (maximum 10 bounces), grenade launcher.

Assign `PlayerCombat.projectilePrefab` to one registered network prefab containing `NetworkIdentity`, `NetworkMatch`, `RoomMember` and `CombatProjectile`. The root does not need a collider or Rigidbody: projectile collision, gravity and movement are server-simulated with the room's isolated `PhysicsScene`, and the component creates a primitive client visual. Add the prefab to `NetworkManager.spawnPrefabs`. `PlayerCombat` calls `MatchManager.PrepareRoomObject` before `NetworkServer.Spawn`, so the prefab's root must contain `NetworkMatch`; `RoomMember` supplies the authoritative room association.

## Input and weapons

The local owner reads LMB, R and keys 1–5 through the Input System only while `PlayerMotor.LocalInputEnabled` is true. It sends trigger/select/reload intent at 30 Hz. The server validates room phase, alive/movement state, input freshness, weapon index, magazine/reserve, reload completion and each weapon's cadence. Switching weapons cancels a reload without awarding ammunition. Death clears trigger/reload state. Respawn restores the prototype loadout.

Pistol and rifle are server hitscan. The ricochet gun performs room-scoped server raycasts, consumes a single total range budget and reflects from surface normals at most 10 times. Rockets use swept sphere casts. Grenades are ballistic capsule projectiles and use capsule casts. Rocket/grenade explosions use the same room's overlap query, apply distance falloff with a 25% minimum inside the configured radius, de-duplicate targets, and may damage the shooter. All balance fields and `hitMask` are editable on `PlayerCombat`.

## Health, death and bunker API

`PlayerCombat` implements `IServerDamageable`:

```csharp
bool IsAlive { get; }
RoomMember Member { get; }
bool ServerApplyDamage(DamageInfo damage);
bool ServerKillEnvironment(string cause);
```

Weapon callers pass their nonzero `netId` and source `RoomMember`; `ServerApplyDamage` rejects a nonzero killer without a matching room association. World hazards such as the bunker call `ServerKillEnvironment("Bunker")` on living players already selected by their own authoritative room/volume validation. Both methods are server-only in intended use; the concrete methods carry Mirror `[Server]` guards. A death is accepted once, disables the motor and authoritative capsule, broadcasts one room-filtered kill notification, then requests `MatchManager.ServerRespawn` after the editable delay. `OnServerRespawn` restores HP, collider and ammunition; the motor independently restores movement and pose through the same listener contract.

The replicated HUD surface is `Health`, `MaxHealth`, `IsDead`, `SelectedWeaponIndex`, `SelectedWeapon`, `MagazineAmmo`, `ReserveAmmo`, `IsReloading`, `ReloadEndsAt` and `ReloadProgress`. `StateChanged` covers SyncVar health/death/selection/reload changes; HUD code may also read ammo properties each frame because magazine/reserve values are Mirror `SyncList<int>` data.

Death presentation hides renderers captured on the player at startup and creates a small client-local primitive Rigidbody ragdoll. Respawn destroys the ragdoll and restores those renderers. This presentation is deliberately art-independent.

## Isolation and validation notes

Every authoritative raycast, sphere/capsule cast and explosion overlap uses `RoomMember.Room.PhysicsScene`; no global `Physics` query is used. Hits and explosions additionally require matching nonempty `RoomMember.MatchId`. Dead colliders/ragdolls are ignored by weapon queries. Fire while dead, stale held triggers, duplicate deaths, reload bypass and cross-room damage are rejected on the server.

Task 06 must wire the player/projectile prefabs before runtime gameplay can be exercised. Task 07 should test each weapon with a host and remote client, projectile registration, reload/switch edge cases, self splash damage, 10-bounce limit, one kill-feed entry per death, ragdoll/respawn and capacity-1 second-room damage isolation.
