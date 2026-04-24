# Il2CppAssemblyFixer

[![Release](https://github.com/mleem97/Il2CppAssemblyFixer/actions/workflows/dotnet-desktop.yml/badge.svg)](https://github.com/mleem97/Il2CppAssemblyFixer/actions/workflows/dotnet-desktop.yml)

A simple tool to repair or fix broken IL2CPP assembly metadata / references in managed code for MelonLoader or BepInEx modding workflows.
---

## The Problem

After certain Unity version updates (e.g. Unity 6000.3.x → 6000.4.x), MelonLoader's Il2CppInterop assembly generator can produce DLLs containing duplicate type definitions (typically the compiler-generated `<>O` delegate cache type). The .NET runtime refuses to load these malformed assemblies, causing **all mods** to fail with:

```
System.BadImageFormatException: Duplicate type with name '<>O' in assembly 'UnityEngine.CoreModule'
```

On top of that, **Unity 6** introduced breaking API changes that crash UnityExplorer:

| # | Error | Root cause |
|---|-------|-----------|
| 1 | `SceneHandle` crash in `SceneHandler.Init()` | `Scene.GetNameInternal` now expects a `SceneHandle`, not `int` |
| 2 | `Method Unstripping Failed` | `SceneManager.GetAllScenes()` stripped by IL2CPP |
| 3 | `ObjectCollectedException` (AssetBundle) | GC collects `Il2CppObject` before managed code accesses it |
| 4 | `TypeLoadException <>c` (PointerStationaryEvent) | Known UniverseLib warning |

**UnityExplorerUnity6Shim** patches all four issues at runtime without modifying UnityExplorer itself.

---

## Quick-Start — Full Setup for Data Center (Unity 6 / MelonLoader 0.7.2)

> This is the recommended path if you just want everything working as fast as possible.

**Step 1 — Install MelonLoader** (if not already done)

1. Download MelonLoader v0.7.2 from [LavaGang/MelonLoader](https://github.com/LavaGang/MelonLoader/releases/tag/v0.7.2).
2. Run `MelonLoader.Installer.exe`, point it at your game EXE and install.
3. Launch the game once and close it — MelonLoader will generate the `Il2CppAssemblies` folder.

**Step 2 — Download this release**

Download the latest release ZIP from the [Releases](../../releases) page. It contains:

```
Il2CppAssemblyFixer_<version>_win-x64.zip   ← standalone EXE (self-contained)
Il2CppAssemblyFixerPlugin_<version>_MelonLoader.zip  ← auto-fix Plugin
UnityExplorerUnity6Shim_<version>.zip        ← Unity 6 crash patches
```

**Step 3 — Fix the Il2Cpp assemblies**

Open PowerShell in the folder where you extracted `Il2CppAssemblyFixer.exe` and run:

```powershell
# Finds the game automatically via the Steam registry
.\Il2CppAssemblyFixer.exe
```

The tool will:
- Scan `MelonLoader\Il2CppAssemblies\` (auto-detected)
- Skip protected Unity core DLLs automatically
- Create a `.bak` backup before overwriting each changed assembly
- Remove all duplicate `<>O` / `<>c` type definitions

**Step 4 — Install the MelonPlugin** (auto-fix on every game start)

Extract the three DLLs from `Il2CppAssemblyFixerPlugin_…_MelonLoader.zip` into your game's `Plugins/` folder:

```
<GameFolder>/
└── Plugins/
    ├── Il2CppAssemblyFixerPlugin.dll
    ├── dnlib.dll
    └── Mono.Cecil.dll
```

**Step 5 — Install UnityExplorerUnity6Shim** (Unity 6 crash fixes)

Extract `UnityExplorerUnity6Shim.dll` from `UnityExplorerUnity6Shim_….zip` into your game's `Mods/` folder:

```
<GameFolder>/
└── Mods/
    └── UnityExplorerUnity6Shim.dll
```

Or let the fixer copy it automatically with `--deploy-shim` (see [EXE flags](#exe-flags) below).

**Step 6 — Launch the game**

Look for these lines in `MelonLoader/Latest.log` to confirm both components are active:

```
[Il2CppAssemblyFixer] Scanning: …\MelonLoader\Il2CppAssemblies
[Shim] UnityExplorer Unity 6 Shim active. F8 = Scene-Dump.
[Shim] SceneHandler.Init patched (SceneHandle-Fix active).
```

Press **F8** in-game at any time to dump the current scene list to the log.

---

## Option A — MelonLoader Plugin (recommended for ongoing use)

`Il2CppAssemblyFixerPlugin.dll` is a **MelonPlugin** — it runs *before* any MelonMod is loaded, so the fix is applied automatically every time you start the game, even after MelonLoader regenerates assemblies.

### Installation

1. Download `Il2CppAssemblyFixerPlugin_<version>_MelonLoader.zip` from [Releases](../../releases).
2. Extract **all three DLLs** into `<GameFolder>/Plugins/`:
   ```
   <GameFolder>/
   └── Plugins/
       ├── Il2CppAssemblyFixerPlugin.dll
       ├── dnlib.dll
       └── Mono.Cecil.dll
   ```
3. Launch the game — duplicates are fixed automatically on every start.

> **Why a Plugin and not a Mod?**  
> MelonLoader loads **Plugins** (`/Plugins/`) before processing Il2Cpp assemblies.  
> **Mods** (`/Mods/`) load *after*, so `BadImageFormatException` would have already occurred. The `OnPreInitialization` hook fires early enough to repair files before they are loaded.

---

## Option B — Standalone EXE (manual / CI / fallback)

### Auto-detect (recommended)

Run without arguments — Steam registry is queried automatically:

```powershell
.\Il2CppAssemblyFixer.exe
```

### Manual path

```powershell
.\Il2CppAssemblyFixer.exe "D:\SteamLibrary\steamapps\common\Data Center\MelonLoader\Il2CppAssemblies"
```

### EXE flags

| Flag | Description |
|------|-------------|
| *(none)* | Process only `Assembly-CSharp*.dll`, skip all protected Unity/Interop DLLs |
| `--all` | Also process every non-skipped DLL in the target directory |
| `--rewrite` | Force a Mono.Cecil metadata rewrite on every assembly, even without duplicates |
| `--restore` | Restore all `.bak` backups and delete them (undo a previous run) |
| `--deploy-shim` | Copy `UnityExplorerUnity6Shim.dll` from next to the EXE into `<GameRoot>/Mods/` |

#### Examples

```powershell
# Normal run — only Assembly-CSharp.dll, with automatic backup
.\Il2CppAssemblyFixer.exe

# Process all non-protected assemblies
.\Il2CppAssemblyFixer.exe --all

# Force Cecil rewrite AND deploy the Unity 6 shim
.\Il2CppAssemblyFixer.exe --rewrite --deploy-shim

# Undo all changes (restores every .bak file and deletes the backups)
.\Il2CppAssemblyFixer.exe --restore
```

### Backup system

Before overwriting any assembly the tool automatically creates a `<name>.dll.bak` file next to the original. Subsequent runs skip files that already have a backup.  
To undo all changes at any time:

```powershell
.\Il2CppAssemblyFixer.exe --restore
```

### Assembly filter

By default the tool only touches game-code assemblies (`Assembly-CSharp*.dll`) and **never** modifies the following protected DLLs:

```
UnityEngine.CoreModule.dll      UnityEngine.UIElementsModule.dll
UnityEngine.IMGUIModule.dll     UnityEngine.TextCoreModule.dll
UnityEngine.InputSystem.dll     UnityEngine.AssetBundleModule.dll
UnityEngine.SceneManagement.dll Il2CppInterop.Runtime.dll
Il2Cppmscorlib.dll              netstandard.dll   mscorlib.dll
UnityExplorer.ML.IL2CPP.CoreCLR.dll
UniverseLib.ML.IL2CPP.Interop.dll
```

Add `--all` to also process other non-protected assemblies.

---

## UnityExplorerUnity6Shim

A lightweight MelonLoader **Mod** (`/Mods/`) that patches UnityExplorer for Unity 6 compatibility at runtime.

### What it fixes

| Patch | Details |
|-------|---------|
| `SceneHandler.Init()` crash | Intercepts the call via Harmony and replaces it with a Unity-6-safe scene enumeration |
| `SceneManager.GetAllScenes()` stripped | Uses `SceneManager.sceneCount` + `GetSceneAt(i)` as fallback — no unstripping required |
| `ObjectCollectedException` | Logged, not re-thrown (UniverseLib fallback path already active) |
| `TypeLoadException <>c` | Fully silenced (known UniverseLib no-op warning) |

### Installation

1. Download `UnityExplorerUnity6Shim_<version>.zip` from [Releases](../../releases).
2. Extract `UnityExplorerUnity6Shim.dll` into `<GameFolder>/Mods/`:
   ```
   <GameFolder>/
   └── Mods/
       └── UnityExplorerUnity6Shim.dll
   ```
3. Launch the game. Confirm in `MelonLoader/Latest.log`:
   ```
   [Shim] UnityExplorer Unity 6 Shim active. F8 = Scene-Dump.
   ```

### F8 Scene-Dump

Press **F8** in-game to print the complete scene list (names, load state, root GameObjects) to `Latest.log` without triggering any SceneHandle API.

---

## When to re-run the fixer

| Situation | Action |
|-----------|--------|
| Game update triggers MelonLoader assembly regeneration | Run `Il2CppAssemblyFixer.exe` (or the Plugin handles it automatically) |
| MelonLoader itself is updated / reinstalled | Run `Il2CppAssemblyFixer.exe` after the first post-update game launch |
| Something went wrong and mods stopped loading | Run `.\Il2CppAssemblyFixer.exe --restore` to revert, then investigate |

---

## Available downloads

| File | Platform | Notes |
|------|----------|-------|
| `Il2CppAssemblyFixer_<ver>_win-x64.zip` | Windows 64-bit | self-contained, no .NET required |
| `Il2CppAssemblyFixer_<ver>_win-x86.zip` | Windows 32-bit | self-contained, no .NET required |
| `Il2CppAssemblyFixer_<ver>_linux-x64.zip` | Linux 64-bit | requires .NET 10 runtime |
| `Il2CppAssemblyFixerPlugin_<ver>_MelonLoader.zip` | Any | MelonPlugin DLL + dependencies |

---

## Building from Source

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and [.NET 6 SDK](https://dotnet.microsoft.com/download/dotnet/6.0).

```bash
git clone https://github.com/mleem97/Il2CppAssemblyFixer
cd Il2CppAssemblyFixer
dotnet build -c Release
```

### Publish standalone EXE (self-contained)

```bash
dotnet publish Il2CppAssemblyFixer.csproj -c Release -r win-x64 --self-contained -p:PublishSingleFile=true
```

### Publish MelonLoader Plugin DLL

```bash
dotnet publish MelonPlugin/Il2CppAssemblyFixerPlugin.csproj -c Release --self-contained false
```

### Build UnityExplorerUnity6Shim

> Requires Unity Engine DLLs from a local game installation. Copy the following files from  
> `<GameFolder>\MelonLoader\Il2CppAssemblies\` into `UnityExplorerUnity6Shim\libs\` before building:
> - `UnityEngine.CoreModule.dll`
> - `UnityEngine.InputSystem.dll`
> - `UnityEngine.SceneManagement.dll`

```bash
dotnet publish UnityExplorerUnity6Shim/UnityExplorerUnity6Shim.csproj -c Release --self-contained false
```

Copy the output `UnityExplorerUnity6Shim.dll` into `<GameFolder>/Mods/`,  
**or** place it next to `Il2CppAssemblyFixer.exe` and run:

```powershell
.\Il2CppAssemblyFixer.exe --deploy-shim
```

