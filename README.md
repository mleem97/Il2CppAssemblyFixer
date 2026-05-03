# Il2CppAssemblyFixer

Repairs corrupted IL2CPP assembly metadata in **Unity 6** games running **MelonLoader v0.7.2+**.

---

## The Problem

Certain Unity updates (e.g., `6000.3.x` → `6000.4.x`) cause MelonLoader's assembly generator to produce malformed DLLs with duplicate type definitions, crashing the game on startup:

```
System.BadImageFormatException: Duplicate type with name '<>O' in assembly 'UnityEngine.CoreModule'
```

### Known Unity 6 Breaking Changes

| Issue | Symptom | Root Cause |
|---|---|---|
| CoreModule Crash | `BadImageFormatException` | Duplicate `<>O` delegate cache types |
| Collections Crash | `ModuleWriterException` in `Unity.Collections.dll` | Nested types referenced via generic TypeSpec not counted |
| Scene Crash | `SceneHandler.Init()` crash | `Scene.GetNameInternal` requires `SceneHandle` |
| Stripping | `Method Unstripping Failed` | `SceneManager.GetAllScenes()` is stripped |
| GC Issues | `ObjectCollectedException` | Premature `Il2CppObject` collection |

---

## Quick-Start

1. Launch the game once so MelonLoader generates the `Il2CppAssemblies` folder.
2. Download the latest [Release ZIP](../../releases).
3. Choose your mode:

**Standalone EXE** — run once after each game update:
```powershell
.\Il2CppAssemblyFixer.exe           # auto-detect game path and fix all DLLs
.\Il2CppAssemblyFixer.exe "D:\Games\Steam\steamapps\common\Data Center\MelonLoader\Il2CppAssemblies"
.\Il2CppAssemblyFixer.exe --rewrite  # force Mono.Cecil metadata rewrite on all assemblies
.\Il2CppAssemblyFixer.exe --restore  # restore all .bak backups
```

**MelonLoader Plugin** — automatic, runs before every mod load:
- Copy `Il2CppAssemblyFixerPlugin.dll`, `dnlib.dll`, and `Mono.Cecil.dll` into `<GameFolder>/Plugins/`.
- The plugin hooks into `OnPreInitialization` and repairs assemblies before the runtime loads them.

---

## Custom / Non-Steam Installs

If the auto-detection fails, create a file named **`game-path.txt`** next to the EXE and paste the path inside:

```
D:\My Games\Data Center\MelonLoader\Il2CppAssemblies
```

The EXE accepts either the `Il2CppAssemblies` directory or the game root.

The auto-detection probes in this order:
1. `game-path.txt` override (if present)
2. Windows registry → `libraryfolders.vdf` (all Steam libraries)
3. Linux/macOS Steam roots (`~/.steam/steam`, `~/.local/share/Steam`, …)
4. All drives A–Z × common Steam folder names (`Steam`, `SteamLibrary`, …)
5. All drives A–Z × common non-Steam folder names (`Games`, `My Games`, `Spiele`, …)
6. User-profile directories (Desktop, Downloads, Documents, …)

---

## Safety & Backups

- Every modified DLL is backed up as `<file>.bak` before changes are written.
- Protected system assemblies (`UnityEngine.CoreModule.dll`, `mscorlib.dll`, etc.) are never modified.
- To undo all changes: `.\Il2CppAssemblyFixer.exe --restore`

---

## How It Works

Fixes run in two phases per assembly:

1. **dnlib** — scans every type for duplicates using a full reference-count walk (including types referenced only through generic `TypeSpec` wrappers). Unreferenced duplicates are removed safely.
2. **Mono.Cecil** — rewrites assembly metadata to normalize the module after structural changes.
