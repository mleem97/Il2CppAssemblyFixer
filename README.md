# Il2CppAssemblyFixer

> Companion repair tool for **gregFramework / gregCore** that repairs duplicate type definitions in MelonLoader-generated IL2CPP assemblies for **Data Center** and other Unity 6 MelonLoader setups.

[![Discord](https://img.shields.io/badge/Discord-Join-5865F2?style=for-the-badge&logo=discord&logoColor=white)](https://discord.gg/greg)
[![gregFramework](https://img.shields.io/badge/gregFramework-Website-blue?style=for-the-badge)](https://gregframework.eu)
[![Build](https://img.shields.io/github/actions/workflow/status/mleem97/Il2CppAssemblyFixer/dotnet-desktop.yml?style=for-the-badge&label=Build%20%26%20Release)](https://github.com/mleem97/Il2CppAssemblyFixer/actions/workflows/dotnet-desktop.yml)
[![Latest Release](https://img.shields.io/github/v/release/mleem97/Il2CppAssemblyFixer?style=for-the-badge)](https://github.com/mleem97/Il2CppAssemblyFixer/releases/latest)
[![License](https://img.shields.io/badge/License-Apache%202.0-green?style=for-the-badge)](./LICENSE)
[![Codacy Badge](https://app.codacy.com/project/badge/Grade/7608e274bcc140b9b17636fe00c135d3)](https://app.codacy.com/gh/mleem97/Il2CppAssemblyFixer/dashboard?utm_source=gh&utm_medium=referral&utm_content=&utm_campaign=Badge_grade)

## Start here

**Use the MelonLoader plugin first.** It is the easiest and recommended installation method on both Windows and Linux/Proton because it runs automatically before MelonMods are loaded.

Only use the standalone fixer when:

- MelonLoader cannot start far enough to load the plugin;
- you need to repair assemblies manually;
- you need `.bak` backups or `--restore`;
- support specifically asks you to run the standalone build.

### Choose your installation

| Your system | Recommended download | Follow this section |
|---|---|---|
| Windows + MelonLoader | `Il2CppAssemblyFixerPlugin_<ver>_MelonLoader.zip` | [Windows: recommended plugin installation](#windows-recommended-plugin-installation) |
| Linux / Steam Deck + Proton + MelonLoader | `Il2CppAssemblyFixerPlugin_<ver>_MelonLoader.zip` | [Linux / Proton: recommended plugin installation](#linux--proton-recommended-plugin-installation) |
| Windows manual repair | `Il2CppAssemblyFixer_<ver>_win-x64.zip` | [Windows: standalone manual repair](#windows-standalone-manual-repair) |
| Linux manual repair | `Il2CppAssemblyFixer_<ver>_linux-x64.zip` | [Linux: standalone manual repair](#linux-standalone-manual-repair) |

> Do not download the Windows standalone EXE for normal Linux/Proton use. The MelonLoader plugin is platform-independent at runtime, while the standalone builds are platform-specific.

## New to modding?

### What these folders mean

After MelonLoader is installed and the game has been started once, the game directory normally contains:

```text
<GameFolder>/
├── <Game>.exe
├── MelonLoader/
│   ├── Il2CppAssemblies/
│   └── Logs/
├── Mods/
├── Plugins/
└── UserLibs/
```

- `Mods/` contains normal gameplay mods.
- `Plugins/` contains MelonLoader plugins that must start early. **Il2CppAssemblyFixerPlugin.dll belongs here.**
- `UserLibs/` contains libraries used by plugins and mods.
- `MelonLoader/Il2CppAssemblies/` contains generated IL2CPP assemblies. The fixer repairs files in this directory.
- `MelonLoader/Logs/` contains MelonLoader logs used for troubleshooting.

Do not place `Il2CppAssemblyFixerPlugin.dll` in `Mods/` or directly beside the game executable.

### Find the game folder

#### Steam on Windows

1. Open Steam.
2. Right-click the game.
3. Select **Manage → Browse local files**.
4. The folder that opens is `<GameFolder>`.

A typical path is:

```text
C:\Program Files (x86)\Steam\steamapps\common\<Game>
```

#### Steam on Linux / Steam Deck

1. Open Steam in Desktop Mode.
2. Right-click the game.
3. Select **Manage → Browse local files**.
4. The folder that opens is `<GameFolder>`.

Typical paths are:

```text
~/.local/share/Steam/steamapps/common/<Game>
~/.steam/steam/steamapps/common/<Game>
<CustomSteamLibrary>/steamapps/common/<Game>
```

The game files are stored in `steamapps/common`. The Proton prefix under `steamapps/compatdata/<APPID>/pfx` is not the game folder and is not where this plugin should be copied.

## Install MelonLoader first

Il2CppAssemblyFixer requires MelonLoader. Use the latest stable MelonLoader version supported by your game. This project is compiled against MelonLoader 0.7.2 and supports the 0.7.2+ setup used by Data Center.

Official MelonLoader resources:

- [MelonLoader repository](https://github.com/LavaGang/MelonLoader)
- [MelonLoader releases](https://github.com/LavaGang/MelonLoader/releases/latest)
- [MelonLoader installer releases](https://github.com/LavaGang/MelonLoader.Installer/releases/latest)

### Windows: install MelonLoader

1. Close the game completely.
2. Download `MelonLoader.Installer.exe` from the official installer releases.
3. Run the installer.
4. Select the game's executable, for example `Data Center.exe`.
5. Select the latest stable MelonLoader version supported by the game.
6. Complete the installation.
7. Start the game once through Steam.
8. Wait until MelonLoader creates `MelonLoader/`, `Plugins/`, `Mods/`, and `UserLibs/`.
9. Close the game.

If those folders were not created, MelonLoader is not installed correctly yet. Fix that before installing Il2CppAssemblyFixer.

### Linux / Proton: install MelonLoader

These steps are for the Windows version of a Unity game running through Steam Proton.

1. In Steam, open **Properties → Compatibility** for the game.
2. Enable **Force the use of a specific Steam Play compatibility tool**.
3. Select a current Proton version supported by the game.
4. Start the unmodded game once, reach the main menu, then close it. This creates the Proton prefix.
5. Install Protontricks. The official project is [Matoking/protontricks](https://github.com/Matoking/protontricks).
6. Find the game's Steam App ID:

```bash
protontricks -s "Data Center"
```

For another game, replace `Data Center` with that game's name.

7. Install the .NET 6 Desktop Runtime in the game's Proton prefix:

```bash
protontricks <APPID> dotnetdesktop6
```

8. Download `MelonLoader.Installer.exe` from the official MelonLoader installer releases.
9. Run the installer inside the correct Proton prefix:

```bash
protontricks-launch --appid <APPID> "$HOME/Downloads/MelonLoader.Installer.exe"
```

10. In the installer, select the game executable inside the real Steam game folder under `steamapps/common/<Game>/`.
11. In Steam, open **Properties → General → Launch Options** and enter exactly:

```bash
WINEDLLOVERRIDES="version=n,b" %command%
```

12. Start the game once through Steam.
13. Wait until MelonLoader creates `MelonLoader/`, `Plugins/`, `Mods/`, and `UserLibs/` in the actual game folder.
14. Close the game.

For Flatpak Protontricks, external EXEs or custom Steam libraries may require additional filesystem permissions. Grant access to the download directory and every custom Steam library before running the installer.

## Recommended plugin installation

Download the latest release and choose:

```text
Il2CppAssemblyFixerPlugin_<ver>_MelonLoader.zip
```

The ZIP contains:

```text
Il2CppAssemblyFixerPlugin.dll
dnlib.dll
Mono.Cecil.dll
fixer_config.json
README.md
```

### Windows: recommended plugin installation

1. Confirm that MelonLoader is installed and the game has been started once.
2. Close the game.
3. Open `<GameFolder>` using **Steam → Manage → Browse local files**.
4. Extract the plugin ZIP to a temporary folder.
5. Copy the files exactly as follows:

```text
Il2CppAssemblyFixerPlugin.dll -> <GameFolder>\Plugins\Il2CppAssemblyFixerPlugin.dll
dnlib.dll                     -> <GameFolder>\UserLibs\dnlib.dll
Mono.Cecil.dll                -> <GameFolder>\UserLibs\Mono.Cecil.dll
fixer_config.json             -> <GameFolder>\MelonLoader\fixer_config.json
```

6. Start the game normally through Steam.
7. Check `<GameFolder>\MelonLoader\Logs\` if the game does not start or the plugin does not appear in the log.

The final structure must look like this:

```text
<GameFolder>\
├── MelonLoader\
│   ├── fixer_config.json
│   └── Il2CppAssemblies\
├── Plugins\
│   └── Il2CppAssemblyFixerPlugin.dll
└── UserLibs\
    ├── dnlib.dll
    └── Mono.Cecil.dll
```

### Linux / Proton: recommended plugin installation

1. Confirm that MelonLoader starts under Proton and that this Steam launch option is still configured:

```bash
WINEDLLOVERRIDES="version=n,b" %command%
```

2. Close the game.
3. Open the real game directory using **Steam → Manage → Browse local files**.
4. Extract the plugin ZIP to a temporary folder.
5. From the extracted folder, copy the files exactly as follows:

```bash
cp "Il2CppAssemblyFixerPlugin.dll" "<GameFolder>/Plugins/Il2CppAssemblyFixerPlugin.dll"
cp "dnlib.dll" "<GameFolder>/UserLibs/dnlib.dll"
cp "Mono.Cecil.dll" "<GameFolder>/UserLibs/Mono.Cecil.dll"
cp "fixer_config.json" "<GameFolder>/MelonLoader/fixer_config.json"
```

Replace `<GameFolder>` with the actual path. Example:

```bash
GAME="$HOME/.local/share/Steam/steamapps/common/Data Center"
cp "Il2CppAssemblyFixerPlugin.dll" "$GAME/Plugins/"
cp "dnlib.dll" "$GAME/UserLibs/"
cp "Mono.Cecil.dll" "$GAME/UserLibs/"
cp "fixer_config.json" "$GAME/MelonLoader/"
```

6. Start the game normally through Steam.
7. Check `<GameFolder>/MelonLoader/Logs/` if the game does not start or the plugin does not appear in the log.

The final structure must look like this:

```text
<GameFolder>/
├── MelonLoader/
│   ├── fixer_config.json
│   └── Il2CppAssemblies/
├── Plugins/
│   └── Il2CppAssemblyFixerPlugin.dll
└── UserLibs/
    ├── dnlib.dll
    └── Mono.Cecil.dll
```

Do not copy the plugin into `steamapps/compatdata/<APPID>/pfx`. The plugin and its dependencies belong in the actual game directory under `steamapps/common/<Game>`.

### What happens after installation?

The plugin runs during `OnPreInitialization`, before MelonMods are loaded. It scans generated assemblies, removes safe unreferenced duplicate type definitions, and records SHA-256 hashes in:

```text
<GameFolder>/MelonLoader/Il2CppAssemblies/.il2cppfixer-manifest
```

Unchanged DLLs are skipped on later launches. Assemblies are processed again after MelonLoader or a game update regenerates or changes them.

The plugin does not create `.bak` backups. The manifest is a processing cache, not a backup.

## Standalone manual repair

### Windows: standalone manual repair

Download:

```text
Il2CppAssemblyFixer_<ver>_win-x64.zip
```

1. Start the game once with MelonLoader so this directory exists:

```text
<GameFolder>\MelonLoader\Il2CppAssemblies\
```

2. Close the game.
3. Extract the Windows ZIP anywhere.
4. Open PowerShell in the extracted folder.
5. Run the fixer with the exact assembly directory:

```powershell
.\Il2CppAssemblyFixer.exe "D:\Games\Data Center\MelonLoader\Il2CppAssemblies"
```

The executable can be stored anywhere. It does not have to be placed in the game directory.

If auto-detection works, this is also valid:

```powershell
.\Il2CppAssemblyFixer.exe
```

Advanced options:

```powershell
.\Il2CppAssemblyFixer.exe "D:\Games\Data Center\MelonLoader\Il2CppAssemblies" --rewrite
.\Il2CppAssemblyFixer.exe "D:\Games\Data Center\MelonLoader\Il2CppAssemblies" --restore
.\Il2CppAssemblyFixer.exe "D:\Games\Data Center\MelonLoader\Il2CppAssemblies" --deploy-shim
```

- The path must point directly to `MelonLoader/Il2CppAssemblies`.
- `--rewrite` rewrites all non-conservative assemblies through Mono.Cecil and should only be used for troubleshooting.
- `--restore` restores every `.dll.bak` file in the target directory and removes the backup files.
- `--deploy-shim` deploys the optional runtime shim.

### Linux: standalone manual repair

Download:

```text
Il2CppAssemblyFixer_<ver>_linux-x64.zip
```

1. Start the game once with MelonLoader through Proton so `MelonLoader/Il2CppAssemblies/` exists.
2. Close the game.
3. Extract the Linux ZIP.
4. Make the binary executable.
5. Run it against the real game directory:

```bash
unzip Il2CppAssemblyFixer_*_linux-x64.zip
chmod +x Il2CppAssemblyFixer
./Il2CppAssemblyFixer "$HOME/.local/share/Steam/steamapps/common/Data Center/MelonLoader/Il2CppAssemblies"
```

For a custom Steam library:

```bash
./Il2CppAssemblyFixer "/mnt/Games/SteamLibrary/steamapps/common/Data Center/MelonLoader/Il2CppAssemblies"
```

Advanced options:

```bash
./Il2CppAssemblyFixer "/path/to/<Game>/MelonLoader/Il2CppAssemblies" --rewrite
./Il2CppAssemblyFixer "/path/to/<Game>/MelonLoader/Il2CppAssemblies" --restore
./Il2CppAssemblyFixer "/path/to/<Game>/MelonLoader/Il2CppAssemblies" --deploy-shim
```

Run the native Linux binary directly. Do not launch it through Proton, Wine, or Protontricks.

## Troubleshooting

### MelonLoader folders do not exist

MelonLoader has not completed its first launch.

1. Remove the fixer files temporarily.
2. Start the game with MelonLoader only.
3. Wait for the game or MelonLoader console to initialize.
4. Close the game.
5. Confirm that `MelonLoader/`, `Plugins/`, `Mods/`, and `UserLibs/` now exist.
6. Install the fixer plugin again.

### Linux: MelonLoader does not start

Confirm that Steam launch options contain exactly:

```bash
WINEDLLOVERRIDES="version=n,b" %command%
```

Also confirm that:

- the game is running as the Windows build through Proton;
- `version.dll` and the `MelonLoader/` folder are beside the game executable;
- .NET 6 Desktop Runtime is installed in the correct game prefix;
- the installer was run with the correct `<APPID>`;
- a custom Steam library is accessible to Flatpak Protontricks.

### Plugin is not listed in the MelonLoader log

Check all four paths:

```text
<GameFolder>/Plugins/Il2CppAssemblyFixerPlugin.dll
<GameFolder>/UserLibs/dnlib.dll
<GameFolder>/UserLibs/Mono.Cecil.dll
<GameFolder>/MelonLoader/fixer_config.json
```

Then inspect the newest file under:

```text
<GameFolder>/MelonLoader/Logs/
```

### `Il2CppAssemblies` is missing or outdated

Launch the game once with MelonLoader. To force MelonLoader to regenerate the assemblies, temporarily add this launch argument after the normal command:

```text
--melonloader.agfregenerate
```

Linux Steam example:

```bash
WINEDLLOVERRIDES="version=n,b" %command% --melonloader.agfregenerate
```

Remove the regeneration argument after one successful launch unless you intentionally want regeneration every time.

### A game update broke the generated assemblies

1. Start the game once with the plugin installed.
2. The plugin detects changed assembly hashes and processes them again.
3. If MelonLoader did not regenerate the assemblies, use `--melonloader.agfregenerate` for one launch.
4. If the plugin cannot load, use the standalone fixer against `MelonLoader/Il2CppAssemblies`.

### Restore standalone backups

Windows:

```powershell
.\Il2CppAssemblyFixer.exe "<GameFolder>\MelonLoader\Il2CppAssemblies" --restore
```

Linux:

```bash
./Il2CppAssemblyFixer "<GameFolder>/MelonLoader/Il2CppAssemblies" --restore
```

The plugin does not create backups, so `--restore` only applies to assemblies previously modified by the standalone fixer.

## Overview

Il2CppAssemblyFixer scans DLLs generated by MelonLoader's IL2CPP assembly pipeline, identifies duplicate type definitions, counts their references, and removes only unreferenced duplicate copies. Modified assemblies are rewritten to normalize their metadata.

It ships as both:

- a **recommended MelonLoader plugin** that repairs assemblies automatically during `OnPreInitialization`; and
- a standalone Windows/Linux fixer for manual repair after game updates or loader failures.

## The problem

Certain Unity 6 and MelonLoader combinations can produce generated DLLs containing multiple type definitions with the same full name. A typical crash looks like this:

```text
System.BadImageFormatException: Duplicate type with name '<>O' in assembly 'UnityEngine.CoreModule'
```

Known cases include:

| Issue | Symptom | Root cause |
|---|---|---|
| CoreModule crash | `BadImageFormatException` | Duplicate compiler-generated delegate cache types |
| Collections write failure | `ModuleWriterException` in `Unity.Collections.dll` | Duplicate types whose references are wrapped in generic `TypeSpec` metadata |

The fixer targets duplicate type-definition and related metadata problems. It does not generally repair method stripping, missing Unity APIs, scene API incompatibilities, or every possible “class/method not found” error.

## Compatibility

| Game | Developer | Loader | Status |
|---|---|---|---|
| Data Center | Waseku | MelonLoader 0.7.2+ | Verified |
| Other Unity 6 IL2CPP titles | Various | MelonLoader 0.7.2+ | Untested; pass the exact `Il2CppAssemblies` path and report issues |

Standalone auto-detection is tailored to **Data Center**. Other games may work when their exact `<GameFolder>/MelonLoader/Il2CppAssemblies/` directory is supplied manually.

## Features

- dnlib-based duplicate type detection and reference-aware removal
- Unsafe duplicate groups with multiple referenced copies are skipped
- Mono.Cecil metadata normalization after structural changes
- Manifest-based plugin cache using `.il2cppfixer-manifest`
- Automatic MelonLoader plugin mode via `OnPreInitialization`
- Standalone Windows and Linux binaries
- Standalone `.bak` backups before modified assemblies are written
- Standalone `--restore` support for reverting backups
- `fixer_config.json` for local logging and telemetry controls

## Release packages

Each release ships three ZIP files:

| ZIP | Contents | Use case |
|---|---|---|
| `Il2CppAssemblyFixerPlugin_<ver>_MelonLoader.zip` | Plugin DLL, `dnlib.dll`, `Mono.Cecil.dll`, config, README | **Recommended automatic repair on Windows and Linux/Proton** |
| `Il2CppAssemblyFixer_<ver>_win-x64.zip` | `Il2CppAssemblyFixer.exe`, config, README | Windows manual repair |
| `Il2CppAssemblyFixer_<ver>_linux-x64.zip` | `Il2CppAssemblyFixer`, config, README | Native Linux manual repair of a Proton game's files |

## Custom / non-Steam installs

If standalone auto-detection fails, create `game-path.txt` next to the standalone executable. Put either the game root or the exact `Il2CppAssemblies` directory on the first line.

Windows example:

```text
D:\Games\Data Center\MelonLoader\Il2CppAssemblies
```

Linux example:

```text
/home/user/Games/Data Center/MelonLoader/Il2CppAssemblies
```

Auto-detection checks:

1. `game-path.txt`
2. `STEAM_HOME` / `STEAM_DIR` and `libraryfolders.vdf`
3. Linux/macOS Steam roots
4. Common Steam library folders on all drives
5. Common custom game folders on all drives
6. User-profile folders such as Desktop, Downloads, and Documents

## Configuration

The fixer reads:

```text
<GameFolder>/MelonLoader/fixer_config.json
```

```jsonc
{
  "logging": {
    "writeLogFile": true,
    "logFileName": "fixer.log",
    "minimumLevel": "Debug"
  },
  "telemetry": {
    "enabled": true,
    "endpoint": "",
    "format": "loki",
    "username": "",
    "apiKey": "",
    "tenantId": "",
    "anonymousId": "",
    "includeAssemblyList": true,
    "includeMachineInfo": true,
    "timeoutSeconds": 5
  }
}
```

`apiKey` is preserved as the JSON field name for backwards compatibility. Internally it is treated as an authentication token. Official release builds may provide embedded telemetry defaults when the corresponding fields are empty; explicit values in `fixer_config.json` take precedence.

`minimumLevel` is currently informational. The local file logger writes all fixer log levels.

## Telemetry

Telemetry is enabled by default and can be configured or disabled in `fixer_config.json`.

Depending on the selected options, the telemetry payload can contain:

- fixer variant, version, run outcome, duration, and result counters;
- a random installation ID and per-run ID;
- modified assembly names when `includeAssemblyList` is enabled;
- operating-system, runtime, architecture, CPU-count, culture, and process information when `includeMachineInfo` is enabled;
- game, Unity, and MelonLoader version information when the plugin can resolve it.

The telemetry payload does not intentionally contain file paths, usernames, hostnames, assembly contents, save files, or game data. Telemetry errors are caught and do not stop the fixer.

To opt out:

```json
"telemetry": { "enabled": false }
```

To keep telemetry enabled while omitting optional assembly and machine details:

```json
"telemetry": {
  "enabled": true,
  "includeAssemblyList": false,
  "includeMachineInfo": false
}
```

See [`README_TELEMETRY.md`](README_TELEMETRY.md) for the full notice.

## Safety and backups

- The standalone fixer skips protected runtime assemblies such as `Il2CppInterop.Runtime.dll`, `mscorlib.dll`, and `netstandard.dll`.
- The standalone fixer processes selected Unity core modules conservatively and only removes compiler-generated duplicate groups from them.
- The standalone fixer creates `<file>.bak` before writing a modified DLL.
- Run the standalone fixer with `--restore` to restore all `.dll.bak` files in the resolved target directory.
- The plugin skips duplicate groups when more than one copy is referenced.
- The plugin only writes an assembly when removable duplicate types were found.
- The plugin does **not** create `.bak` backups; `.il2cppfixer-manifest` is only a hash cache.
- Both variants catch processing errors and continue where possible.

## Upstream migration

This repository has been aligned with `leoms1408/Il2CppAssemblyFixer:master` where compatible with the current gregFramework codebase. The original upstream is a compact dnlib-only .NET 6 fixer. This repository keeps the newer standalone app, MelonLoader plugin, shared helpers, telemetry controls, CI workflow, and gregFramework documentation while carrying forward the relevant upstream dependency baseline (`dnlib` 4.5.0) and duplicate-type repair intent.

## Build from source

Requirements:

- .NET 10 SDK for the standalone app
- .NET 6 SDK for the MelonLoader plugin

```bash
git clone https://github.com/mleem97/Il2CppAssemblyFixer.git
cd Il2CppAssemblyFixer
dotnet restore Il2CppAssemblyFixer.sln
dotnet build -c Release
```

## Repository layout

```text
.
├── Program.cs                         # Standalone fixer entry point
├── MelonPlugin/                       # MelonLoader plugin
├── Shared/                            # Config, telemetry, logging, dnlib helpers
├── scripts/                           # Build/release helper scripts
├── .github/workflows/                 # CI and release workflow
├── fixer_config.json                  # Default runtime config
├── README_TELEMETRY.md                # Full telemetry notice
├── LICENSE                            # Apache 2.0
└── README.md
```

## Links

- **Repository:** [github.com/mleem97/Il2CppAssemblyFixer](https://github.com/mleem97/Il2CppAssemblyFixer)
- **Releases:** [github.com/mleem97/Il2CppAssemblyFixer/releases](https://github.com/mleem97/Il2CppAssemblyFixer/releases)
- **Issues:** [github.com/mleem97/Il2CppAssemblyFixer/issues](https://github.com/mleem97/Il2CppAssemblyFixer/issues)
- **gregCore:** [github.com/mleem97/gregCore](https://github.com/mleem97/gregCore)
- **Discord / Support:** [discord.gg/greg](https://discord.gg/greg)
- **Website:** [gregframework.eu](https://gregframework.eu)

## Credits

| Role | Contributor |
|---|---|
| Original compact fixer | [leoms1408](https://github.com/leoms1408) |
| gregFramework codebase and integration | [mleem97](https://github.com/mleem97) / TeamGreg Modding |
| Framework ecosystem | [gregCore](https://github.com/mleem97/gregCore) / gregFramework |

## License

This project is licensed under the **Apache License 2.0**. See [`LICENSE`](./LICENSE).

## Join the gregFramework team

gregFramework is a community-driven modding ecosystem for Data Center. Contributors are welcome across code, documentation, testing, infrastructure, and community support.

- Discord: [discord.gg/greg](https://discord.gg/greg)
- Website: [gregframework.eu](https://gregframework.eu)
- Email: apply@gregframework.eu

---

**gregFramework — powered by the community.**
