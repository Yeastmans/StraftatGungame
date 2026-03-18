# Gun Game — STRAFTAT Mod (Beta)

Classic Gun Game mode for [STRAFTAT](https://store.steampowered.com/app/2412110/STRAFTAT/). Get a kill, get a new weapon. First player to frag through every weapon wins.

## Features

- **Full weapon progression** — cycle through 60+ weapons from pistols to melee, configurable via config file
- **Automatic weapon swaps** — killer receives the next weapon immediately on kill
- **Respawn with correct weapon** — dead players respawn with whatever weapon they're up to
- **Map spawners disabled** — no picking up random weapons off the ground
- **Configurable** — set kill target, respawn delay, and full weapon order

### Manual Install
1. Install [BepInEx 5](https://thunderstore.io/c/straftat/p/BepInEx/BepInExPack/) for STRAFTAT
2. Copy `GunGameMod.dll` into `STRAFTAT/BepInEx/plugins/`
3. Launch the game and host a match

## Configuration

After first launch, a config file is created at:
```
STRAFTAT/BepInEx/config/com.modder.gungame.cfg
```

| Setting | Default | Description |
|---|---|---|
| `General.Enabled` | `true` | Enable/disable Gun Game mode |
| `General.Kills To Win` | `66` | Kills needed to win the round |
| `General.Respawn Delay` | `3` | Seconds before a dead player respawns |
| `Weapons.Weapon Order` | *(full list)* | Comma-separated weapon progression — see below |

### Weapon Order
To customize, edit `Weapon Order` in the config. Names must match the game's prefab names exactly. 
```
Gun, Glock, Revolver, Silenzzio, Webley, Keso, Bender, BeamLoad,
Mac10, SMG, Bukanee, Dispenser, Yangtse, Hill_H15, Crisis, DF_Torrent, GlaiveGun,
Tromblonj, SawedOff, Shotgun, Havoc, AAA12,
Kusma, AR15, AK-K, QCW05, FG42, HK_G11, HK_Caws, SmithCarbine, Gust,
Warden, Kanye, Elephant, M2000, Bayshore, HandCanon,
Minigun, Nugget, Mortini, DualLauncher, RocketLauncher, Prophet, Phoenix, Gamma, GammaGen2,
BlankState, Bublee, DF_Blister, DF_Cyst,
HandGrenade, GlandGrenade,
ProximityMine, APMine, Claymore,
BaseballBat, Stylus, Nizeh, JahvalMahmaerd, BigFattyBro, CurvedKnife, Couperet, Katana, Flamberge, DF_GodSword, Impetus
```

## How It Works

1. All players spawn with the first weapon in the progression
2. When you kill someone, your current weapon is despawned and you receive the next one
3. The player you killed respawns with whatever weapon they were on
4. First player to reach the configured kill count wins the round
5. Map weapon spawners are disabled — the only weapon you get is your Gun Game weapon

## Building from Source

Requires:
- .NET Framework 4.7.2
- BepInEx 5 + Harmony
- STRAFTAT game assemblies (Assembly-CSharp.dll, FishNet.Runtime.dll, etc.)
