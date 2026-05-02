# Changelog

All notable changes to this project will be documented in this file.

The format is based on "Keep a Changelog" (https://keepachangelog.com/en/1.0.0/).

## [Unreleased]

### Added
- 

### Changed
- 

### Fixed
- 

### Removed
- 

## [0.2.0] - 2026-05-02

### Added
- MelonLoader plugin (`MelonPlugin/Il2CppAssemblyFixerPlugin.dll`) for automatic duplicate-type fixes before mods load.
- `build.ps1` script to build Windows/Linux binaries and MelonLoader plugin.
- Repository automation: `AGENTS.md`, `CHANGELOG.md`, commit-msg hooks for Conventional Commits, and scripts for version tagging.

### Fixed
- Reference-aware duplicate type removal prevents TypeDef removal when still in use.

### Removed
- Removed `UnityExplorerUnity6Shim` project from repository.

### Changed
- Commit messages normalized to Conventional Commits format (`scope(type): message`).

## [0.1.0] - 2026-05-02

### Added
- Reference-aware duplicate type removal to prevent dnlib ModuleWriterException when types are still referenced.
- Single-file Windows and Linux executable builds.

### Fixed
- Fixed crash when removing duplicate type definitions — now checks if duplicates are referenced before removal.

### Changed
- Improved assembly processing and error messages.
