# Building GunGameMod from Source

## Prerequisites

- [Visual Studio 2022](https://visualstudio.microsoft.com/) (Community edition works fine)
  - Make sure the **.NET desktop development** workload is installed
- [STRAFTAT](https://store.steampowered.com/app/2412110/STRAFTAT/) installed with BepInEx
- [BepInEx 5](https://thunderstore.io/c/straftat/p/BepInEx/BepInExPack/) installed into your STRAFTAT folder

## Project Structure

```
GunGameMod/
├── GunGameMod.csproj         ← project file (open this in Visual Studio)
├── GunGameMod.sln            ← solution file
├── Plugin.cs                 ← main plugin entry point, config, weapon order API
├── GunGamePatches.cs         ← all Harmony patches (kill tracking, respawn, weapon flow)
├── GunGameWeaponManager.cs   ← weapon spawning, despawning, and hand placement
├── manifest.json             ← Thunderstore package manifest
│
└── libs/                     ← reference DLLs (not included — you copy these yourself)
    ├── BepInEx/
    │   ├── BepInEx.dll
    │   └── 0Harmony.dll
    │
    └── GameAssemblies/
        ├── Assembly-CSharp.dll
        ├── FishNet.Runtime.dll
        ├── UnityEngine.dll
        ├── UnityEngine.CoreModule.dll
        ├── UnityEngine.PhysicsModule.dll
        ├── UnityEngine.AnimationModule.dll
        ├── Unity.InputSystem.dll
        ├── UnityEngine.UI.dll
        ├── Unity.TextMeshPro.dll
        └── DOTween.dll
```

## Step 1 — Find your STRAFTAT install folder

If you installed via Steam, right-click STRAFTAT in your library → **Manage** → **Browse local files**. This opens the game folder, typically:

```
C:\Program Files (x86)\Steam\steamapps\common\STRAFTAT\
```

You'll need files from two subfolders:
- `STRAFTAT\BepInEx\core\` — BepInEx framework DLLs
- `STRAFTAT\STRAFTAT_Data\Managed\` — game and Unity engine DLLs

## Step 2 — Copy BepInEx DLLs

Create the folder `libs\BepInEx\` inside the project directory if it doesn't exist, then copy these two files from `STRAFTAT\BepInEx\core\`:

| File | Source |
|------|--------|
| `BepInEx.dll` | `STRAFTAT\BepInEx\core\BepInEx.dll` |
| `0Harmony.dll` | `STRAFTAT\BepInEx\core\0Harmony.dll` |

## Step 3 — Copy game assemblies

Create the folder `libs\GameAssemblies\` inside the project directory if it doesn't exist, then copy these files from `STRAFTAT\STRAFTAT_Data\Managed\`:

| File | What it provides |
|------|-----------------|
| `Assembly-CSharp.dll` | All game classes (GameManager, PlayerPickup, SpawnerManager, etc.) |
| `FishNet.Runtime.dll` | Networking (ServerManager, NetworkObject, SyncVars) |
| `UnityEngine.dll` | Core Unity runtime |
| `UnityEngine.CoreModule.dll` | GameObject, Transform, MonoBehaviour |
| `UnityEngine.PhysicsModule.dll` | Rigidbody, Collider |
| `UnityEngine.AnimationModule.dll` | Animation components |
| `Unity.InputSystem.dll` | Input handling |
| `UnityEngine.UI.dll` | UI components |
| `Unity.TextMeshPro.dll` | Text rendering (chat messages) |
| `DOTween.dll` | Tween animations |

## Step 4 — Open and build

1. Open `GunGameMod.sln` (or `GunGameMod.csproj`) in Visual Studio
2. All references should resolve with no yellow warning triangles — if you see warnings, double-check that every DLL is in the correct `libs\` subfolder
3. Set the build configuration to **Release** (dropdown in the toolbar)
4. Build the solution: **Build** → **Build Solution** (`Ctrl+Shift+B`)
5. The output DLL will be at: `bin\Release\net48\GunGameMod.dll`

## Step 5 — Install the mod

Copy `GunGameMod.dll` into your STRAFTAT plugins folder:

```
STRAFTAT\BepInEx\plugins\GunGameMod.dll
```

Launch the game and host a match. Gun Game mode activates automatically.

## Step 6 — Configure (optional)

After the first launch with the mod installed, a config file is generated at:

```
STRAFTAT\BepInEx\config\com.modder.gungame.cfg
```

Open it in any text editor to change settings like kill count, respawn delay, or weapon order. Changes take effect next time you host a match.

## Troubleshooting

| Problem | Fix |
|---------|-----|
| Yellow warning triangles on references | Make sure all DLLs are in `libs\BepInEx\` and `libs\GameAssemblies\` with the exact filenames listed above |
| Build error about target framework | Install the .NET Framework 4.8 targeting pack from the Visual Studio Installer |
| Mod doesn't load in-game | Confirm BepInEx is installed correctly — you should see a `BepInEx\LogOutput.log` file after launching |
| Mod loads but nothing happens | You must be the host (server) for Gun Game to activate |
