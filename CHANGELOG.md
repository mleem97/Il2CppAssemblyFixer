# Changelog

All notable changes to this project will be documented in this file.
Format: [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) · Versioning: [SemVer](https://semver.org/)

## [Unreleased]

## [0.3.0] - 2026-05-03

### Added
- **`game-path.txt` config override** — place next to the EXE to point to any custom or non-Steam install path (accepts either the `Il2CppAssemblies` dir or the game root).
- **Non-Steam / custom install detection** — drive scan now also checks `Games`, `MyGames`, `Spiele`, `Program Files`, and 10+ other common parent folders without the `steamapps/common/` prefix.
- **User-profile directory scan** — searches Desktop, Downloads, Documents, Documents\Games, and LocalAppData as fallback locations.
- **Linux/macOS Steam roots** — probes `~/.steam/steam`, `~/.local/share/Steam`, Snap Steam, and `/opt/steam` before falling back to drive scan.
- **`HKEY_CURRENT_USER` registry fallback** for Steam path detection.
- **`libraryfolders.vdf` parser** — reads all configured Steam library paths so multi-library setups are fully covered.

### Fixed
- **Unity.Collections.dll crash (Issue #6)** — `BuildTypeReferenceCounts` now follows `TypeSpec` wrappers when scanning `MemberRef`/`IMethodDefOrRef`/`IField` operands. Nested types referenced only through generic instantiations are no longer incorrectly removed, preventing the `ModuleWriterException`.

### Changed
- EXE now processes **all non-skipped assemblies** by default instead of only `Assembly-CSharp*.dll`. The `--all` flag and `_processAll` field were removed.
- Auto-detection restructured into 5 explicit stages with detailed debug output at each step.
- CI workflow: replaced `win-x86` target (broken with .NET 10 single-file) with **linux-x64 self-contained** single-file; upgraded `softprops/action-gh-release` to v2; releases now only trigger on `v*` tags.

### Removed
- `--all` CLI flag (now the default behaviour).
- `FINAL_STATUS.md`, `COMMIT_AUDIT.md` — internal one-off AI session artefacts.
- `scripts/rewrite-msg.ps1`, `scripts/rewrite-msg.sh` — one-time history-rewrite scripts.

## [0.2.0] - 2026-05-02

### Added
- MelonLoader plugin (`MelonPlugin/Il2CppAssemblyFixerPlugin.dll`) for automatic duplicate-type fixes before mods load.
- `build.ps1` script to build Windows/Linux binaries and MelonLoader plugin.
- Repository automation: `AGENTS.md`, `CHANGELOG.md`, commit-msg hooks for Conventional Commits, and scripts for version tagging.

### Fixed
- Reference-aware duplicate type removal prevents TypeDef removal when still in use.

### Removed
- `UnityExplorerUnity6Shim` project removed from repository.

### Changed
- Commit messages normalised to Conventional Commits format.

## [0.1.0] - 2026-05-02

### Added
- Reference-aware duplicate type removal to prevent `ModuleWriterException` when types are still referenced.
- Single-file Windows and Linux executable builds.

### Fixed
- Crash when removing duplicate type definitions — now checks reference count before removal.

### Changed
- Improved assembly processing pipeline and error messages.
