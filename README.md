# Il2CppAssemblyFixer

Repairs corrupted IL2CPP assembly metadata in **Unity 6** games running **MelonLoader v0.7.2+**.

[![.NET Build & Release](https://img.shields.io/github/actions/workflow/status/mleem97/Il2CppAssemblyFixer/dotnet-desktop.yml?style=for-the-badge&label=Build%20%26%20Release)](https://github.com/mleem97/Il2CppAssemblyFixer/actions/workflows/dotnet-desktop.yml)
[![Latest Release](https://img.shields.io/github/v/release/mleem97/Il2CppAssemblyFixer?style=for-the-badge)](https://github.com/mleem97/Il2CppAssemblyFixer/releases/latest)
[![License](https://img.shields.io/github/license/mleem97/Il2CppAssemblyFixer?style=for-the-badge)](LICENSE)
[![Codacy Badge](https://app.codacy.com/project/badge/Grade/7608e274bcc140b9b17636fe00c135d3)](https://app.codacy.com/gh/mleem97/Il2CppAssemblyFixer/dashboard?utm_source=gh&utm_medium=referral&utm_content=&utm_campaign=Badge_grade)

> **Tested compatibility:** so far this tool has only been verified against **Data Center** by **Waseku**.
> If you would like support for another Il2Cpp Unity 6 title, please [open an issue](https://github.com/mleem97/Il2CppAssemblyFixer/issues/new) — including the game name, Unity version and the MelonLoader error message — and I will look into it.

---

## The Problem

Certain Unity updates (e.g. `6000.3.x` → `6000.4.x`) cause MelonLoader's assembly generator to produce malformed DLLs with duplicate type definitions, crashing the game on startup:

```
System.BadImageFormatException: Duplicate type with name '<>O' in assembly 'UnityEngine.CoreModule'
```

### Known Unity 6 breaking changes

| Issue              | Symptom                                            | Root cause                                            |
|--------------------|----------------------------------------------------|-------------------------------------------------------|
| CoreModule crash   | `BadImageFormatException`                          | Duplicate `<>O` delegate cache types                  |
| Collections crash  | `ModuleWriterException` in `Unity.Collections.dll` | Nested types referenced via generic TypeSpec not counted |
| Scene crash        | `SceneHandler.Init()` crash                        | `Scene.GetNameInternal` requires `SceneHandle`        |
| Stripping          | `Method Unstripping Failed`                        | `SceneManager.GetAllScenes()` is stripped             |
| GC issues          | `ObjectCollectedException`                         | Premature `Il2CppObject` collection                   |

---

## Release packages

Each release ships **three** ZIPs — pick the one that matches how you want to use the fixer:

| ZIP                                                | Contents                                                                                  | When to use                                                             |
|----------------------------------------------------|-------------------------------------------------------------------------------------------|-------------------------------------------------------------------------|
| `Il2CppAssemblyFixer_<ver>_win-x64.zip`            | `Il2CppAssemblyFixer.exe` + `fixer_config.json` + `README.md`                             | Windows, run manually after a game update                              |
| `Il2CppAssemblyFixer_<ver>_linux-x64.zip`          | `Il2CppAssemblyFixer` (ELF binary) + `fixer_config.json` + `README.md`                    | Linux / Proton users, run manually                                      |
| `Il2CppAssemblyFixerPlugin_<ver>_MelonLoader.zip`  | `Il2CppAssemblyFixerPlugin.dll`, `dnlib.dll`, `Mono.Cecil.dll` + `fixer_config.json` + `README.md` | "Set and forget" — runs automatically on every MelonLoader start       |

### How to install each pack

#### 1. Standalone EXE (Windows)

1. Launch the game **once** so MelonLoader generates `<GameFolder>/MelonLoader/Il2CppAssemblies/`.
2. Extract the `win-x64` ZIP anywhere.
3. Double-click `Il2CppAssemblyFixer.exe` (auto-detects the game path), or run from a terminal:
   ```powershell
   .\Il2CppAssemblyFixer.exe                                                        # auto-detect
   .\Il2CppAssemblyFixer.exe "D:\Games\…\Data Center\MelonLoader\Il2CppAssemblies"  # explicit
   .\Il2CppAssemblyFixer.exe --rewrite                                              # force Mono.Cecil rewrite
   .\Il2CppAssemblyFixer.exe --restore                                              # restore all .bak backups
   ```
4. The first run drops `fixer_config.json` and `fixer.log` into `<GameFolder>/MelonLoader/`. The bundled `fixer_config.json` from the ZIP can be copied there manually if you prefer.

#### 2. Standalone binary (Linux / Proton)

```bash
unzip Il2CppAssemblyFixer_*_linux-x64.zip
chmod +x Il2CppAssemblyFixer
./Il2CppAssemblyFixer "/path/to/Data Center/MelonLoader/Il2CppAssemblies"
```

Auto-detection probes typical Steam paths under `~/.steam/steam`, `~/.local/share/Steam`, and a few common alternatives. If none of them match, pass the path explicitly or drop a `game-path.txt` file (see below).

#### 3. MelonLoader Plugin (recommended)

1. Open the `MelonLoader.zip`.
2. Copy these three DLLs into `<GameFolder>/Plugins/`:
   - `Il2CppAssemblyFixerPlugin.dll`
   - `dnlib.dll`
   - `Mono.Cecil.dll`
3. Copy `fixer_config.json` into `<GameFolder>/MelonLoader/` (next to `Latest.log`). If you skip this step the plugin creates one with default values on its first run.
4. Launch the game. The plugin hooks `OnPreInitialization` and repairs assemblies **before** any mod is loaded — no manual intervention after future updates.

---

## Custom / non-Steam installs

If auto-detection fails, drop a file named **`game-path.txt`** next to the EXE/binary with the absolute path inside:

```
D:\My Games\Data Center\MelonLoader\Il2CppAssemblies
```

The EXE accepts either the `Il2CppAssemblies` directory or the game root. Probing order:

1. `game-path.txt` override (if present)
2. Windows registry → `libraryfolders.vdf` (all Steam libraries)
3. Linux/macOS Steam roots (`~/.steam/steam`, `~/.local/share/Steam`, …)
4. All drives A–Z × common Steam folder names (`Steam`, `SteamLibrary`, …)
5. All drives A–Z × common non-Steam folder names (`Games`, `My Games`, `Spiele`, …)
6. User-profile directories (Desktop, Downloads, Documents, …)

---

## Configuration: `fixer_config.json`

The fixer reads `<GameFolder>/MelonLoader/fixer_config.json` on every run. The release ZIPs ship a pre-populated copy; the EXE/plugin will create one with safe defaults if the file is missing.

```jsonc
{
  "logging": {
    "writeLogFile": true,            // mirror everything to fixer.log
    "logFileName":  "fixer.log",
    "minimumLevel": "Debug"
  },
  "telemetry": {
    "enabled":     true,             // see "Telemetry" below for opt-out
    "endpoint":    "https://loki.example.com/loki/api/v1/push",
    "format":      "loki",           // "loki" | "json"
    "username":    "",               // optional Basic-auth user
    "apiKey":      "",               // Basic-auth password / Bearer token
    "tenantId":    "",               // Loki X-Scope-OrgID (multi-tenant only)
    "anonymousId": "",               // auto-generated UUID, do not edit
    "includeAssemblyList": true,
    "includeMachineInfo":  true,
    "timeoutSeconds": 5
  }
}
```

A run-by-run log is written to `<GameFolder>/MelonLoader/fixer.log` — same folder as MelonLoader's own `Latest.log`. To turn the log file off, set `logging.writeLogFile` to `false`.

---

## Telemetry (opt-out)

To help me develop and improve this tool I have integrated a small telemetry service that reports **which errors are occurring when starting** plus a few non-identifying counters (number of assemblies scanned, number of duplicate types removed, run duration). The endpoint is a self-hosted Grafana Loki instance — no third-party processors are involved.

**What is sent**

* Tool variant (`exe` / `plugin`) and version
* Operating system
* Counters: assemblies scanned / modified, duplicate types removed, errors
* Run duration in milliseconds
* A randomly-generated, stable `anonymousId` (so repeated runs from the same install can be grouped — it is **not** linked to your name, IP, account, hostname or any other identifier)

**What is NOT sent**

* No file paths, no usernames, no hostnames
* No assembly contents, no game data, no save files
* Nothing at all if your network is offline — telemetry fails silently and never blocks the fixer

### How to opt out

If you do not wish to participate, open `<GameFolder>/MelonLoader/fixer_config.json` and set:

```json
"telemetry": { "enabled": false }
```

That single change is enough — every other field can stay as it is. The next run will not contact any server. See [`README_TELEMETRY.md`](README_TELEMETRY.md) (also included in every release ZIP) for the full notice.

---

## Safety & backups

- Every modified DLL is backed up as `<file>.bak` before changes are written.
- Protected runtime assemblies (`Il2CppInterop.Runtime.dll`, `mscorlib.dll`, `netstandard.dll`, …) are never touched.
- Unity core modules (`UnityEngine.CoreModule.dll`, `UnityEngine.IMGUIModule.dll`, …) are processed in **conservative mode**: only compiler-generated duplicate types (`<>O`, `<>c`, …) are considered, and the file is rewritten only if duplicates were actually removed.
- To undo all changes: `.\Il2CppAssemblyFixer.exe --restore`

---

## How it works

Per assembly, two phases run:

1. **dnlib** — scans every type for duplicates using a full reference-count walk (including types referenced only through generic `TypeSpec` wrappers). Unreferenced duplicates are removed safely. For Unity core modules a stricter filter limits removal to compiler-generated names.
2. **Mono.Cecil** — rewrites assembly metadata to normalize the module after structural changes.

---

## Compatibility

| Game           | Developer | Status                            |
|----------------|-----------|-----------------------------------|
| Data Center    | Waseku    | ✅ Verified (1.0.49.1, 1.0.50.3)  |
| _other titles_ | _–_       | ❓ Untested — please [open an issue](https://github.com/mleem97/Il2CppAssemblyFixer/issues/new) |

Want support for another Il2Cpp Unity 6 game? Open an issue with:

- Game name and developer
- Unity version (visible in MelonLoader's `Latest.log`)
- The exact `BadImageFormatException` / `ModuleWriterException` line you are seeing
- A copy of `<GameFolder>/MelonLoader/Latest.log`

I cannot promise compatibility with everything — but a quick triage is very much possible.
