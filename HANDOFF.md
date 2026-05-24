# Gun Game Handoff

Branch: `codex-respawn-watchdog`

Last pushed commit at handoff: `27ab65f Use actual held hand for refill drops`

Current C-drive test deploy:

`C:\Steam\steamapps\common\STRAFTAT\BepInEx\plugins\GunGameMod.dll`

The last deployed DLL hash from the final test build was:

`AAB395CDB741C85A4C785E8DB1C9285BED467148A007EF80E150A81241AAC8E7`

## What Changed

### Respawn Safety

- Respawn retry/rescue logic was added for players who get stuck between rounds.
- Respawn now waits `Respawn Delay + 3` seconds before attempting the rescue.
- Respawn validation checks that the player object exists, is active, has health, is not killed, has `PlayerPickup`, and can move.
- Rescue attempts are capped and logged with `[GunGame Respawn]`.

Main file:

- `src/GunGamePatches.cs`

### Mod Menu Config

- Gun Game now exposes 100 weapon progression slots.
- Each slot has a primary weapon and an offhand weapon:
  - `Weapon 1`
  - `Weapon 1 Off Hand`
  - ...
  - `Weapon 100`
  - `Weapon 100 Off Hand`
- Offhand dropdowns include `None`.
- `Kills To Win` is configurable from `1` to `100`.
- Dropdown choices are alphabetized.
- Dropdown width is clamped to a fixed width so selected rows do not stretch wider than the rest.

Main file:

- `src/Plugin.cs`

### Weapon Discovery And Compatibility

- The mod does not call `SpawnerManager.PopulateAllWeapons()`.
- Weapon options are discovered with `Resources.LoadAll<GameObject>("RandomWeapons")`.
- That resource lookup is now cached so it is not called repeatedly during weapon grants/refills.
- Missing weapon names are cached too, so bad/missing config values do not repeatedly rescan resources.
- Koki Weapons and CMR are soft dependencies:
  - Koki GUID: `com.koki.weapons`
  - CMR GUID: `straftatcmr.rebalance`
- Koki aliases are only active if Koki is loaded.
- CMR aliases are only active if CMR is loaded.

Main file:

- `src/Plugin.cs`

### Dual Wield / Offhand Progression

- Gun Game can now give a second configured weapon in the left hand.
- Right hand uses `Weapon N`.
- Left hand uses `Weapon N Off Hand`.
- If offhand is `None`, only the primary weapon is given.
- Non-host left-hand stabilization was added so offhand weapons attach correctly for remote players.
- `RightHandPickup`, `LeftHandPickup`, and weapon switching are blocked while Gun Game is enabled so players cannot bypass progression by picking up/dropping world weapons.

Main files:

- `src/GunGameWeaponManager.cs`
- `src/GunGamePatches.cs`

### Per-Hand Refill

- When a weapon is consumed/dropped from one hand, Gun Game now refills only that hand.
- Right-hand refill pulls from `Weapon N`.
- Left-hand refill pulls from `Weapon N Off Hand`.
- Full `GiveWeaponToPlayer()` is still used for progression changes, initial gives, and respawns.
- Per-hand `ReplaceWeaponInHand()` is used for consumable/drop refills.

Main files:

- `src/GunGameWeaponManager.cs`
- `src/GunGamePatches.cs`

### Koki Weapon Handling

- Teleport Mine uses Koki's default behavior: it is not manually copied/linked by Gun Game.
- Teleport Mine receives ammo/count `2` when spawned so both mines can be placed through the default Koki flow.
- Teleport Mine is treated as a placeable.
- Repulsion Grenade is treated as a throwable, not a placeable.
- Throwable items detach and are not destroyed by Gun Game.
- Placeable mine replacement is per hand.
- Teleport mine waiting is hand-specific during per-hand refill, so a right-hand throwable refill does not wait for left-hand teleport mines.

Main files:

- `src/GunGameWeaponManager.cs`
- `src/GunGamePatches.cs`

## What Is Being Worked On / Needs Testing

The active testing area is Koki dual-wield interaction, especially:

- Right hand Repulsion Grenade plus left hand Teleport Mine.
- Placing both teleport mines should not start the fuse on the grenade in the other hand.
- When teleport mines are replaced, only the actual teleport mine hand should be touched.
- When a throwable is used, only that hand should refill.
- When a teleport mine is placed, spawned mine objects should not be mistaken for the held weapon object.

The most recent fix changed drop/refill hand detection:

- The drop hook no longer trusts the RPC's `rightHand` flag.
- It checks whether the dropped object is actually the current right-hand object or current left-hand object.
- If the dropped object is not held in either hand, Gun Game lets the original game/mod behavior handle it.

This was added because teleport mine placement can involve spawned mine objects that should not trigger Gun Game hand cleanup.

## Known Risk Areas

- `PlayerPickup.HandsReconstruct()` in STRAFTAT can throw when the player is temporarily left-hand-only. A prefix handles the left-only case while Gun Game is enabled.
- Koki's Teleport Mine relies on its own placement/linking flow. Avoid reintroducing manual TPTrap copying/linking.
- The game RPC `rightHand` bool may not always describe the hand Gun Game should refill, so use actual held object checks.
- Per-hand refill is safer for consumables, but progression upgrades still intentionally replace both hands.
- If a future custom weapon mod adds weapons after Gun Game has already cached resources, a forced cache refresh hook may be needed.

## Deployment Notes

Current C-drive test plugin folder should include:

- `GunGameMod.dll`
- `ModMenu\ModMenu.dll`
- `KokiWeapons.dll`
- `components.dll`
- `MyceliumNetworkingForStraftat.dll`
- Koki asset files:
  - `kokiWeaponsBundle`
  - `repulsiongrenade`
  - `shared`
  - `tptrap`
- `StraftatCMR.dll`

Only deploy to C-drive unless explicitly asked otherwise:

`C:\Steam\steamapps\common\STRAFTAT\BepInEx\plugins`

## Build Notes

Before building, recreate `src\libs` from:

- `C:\Users\kiran\GUNGAME LATEST BUILD\STRAFTAT Gungame build\libs\BepInEx`
- `C:\Users\kiran\GUNGAME LATEST BUILD\STRAFTAT Gungame build\libs\GameAssemblies`

Build command:

`dotnet build src\GunGameMod.csproj -c Release`

After building, copy:

`src\bin\Release\net48\GunGameMod.dll`

to:

`release\GunGameMod.dll`

and to the C-drive plugin folder.

Clean generated folders after build:

- `src\bin`
- `src\obj`
- `src\libs`

## Recent Commit Trail

- `27ab65f` Use actual held hand for refill drops
- `e1bc8ec` Only refill actual held drops
- `5ac8cdf` Treat repulsion grenade as throwable
- `6639654` Handle Koki placeable refills per hand
- `19b8e9e` Cache resource weapon discovery
- `f5f7edb` Allow legitimate protected hand refills
- `c3ec72e` Sort and align weapon dropdowns
- `404d35c` Refill dropped dual wield weapons per hand
- `1ea65a0` Stabilize offhand grants for non-hosts
- `ee846c5` Add CMR compatibility and protect weapon grants
- `a9e5c73` Pair weapon progression config entries
- `cf54157` Use default teleport mine linking behavior
