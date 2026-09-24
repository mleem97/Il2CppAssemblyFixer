# AGENTS.md — Notes for AI agents (Il2CppAssemblyFixer)

Repo: https://github.com/mleem97/Il2CppAssemblyFixer · License: Apache-2.0 · Version: see `VERSION` (0.1.0).

Standalone fixer tool plus MelonPlugin for Data Center: repairs IL2CPP
interop assemblies. Excluded from the central `build.sh` mod discovery —
build it directly.

## Duties

1. **Read first:** `README.md`, `QUICKSTART.md`, `docs/INDEX.md` (if present),
   `CONTRIBUTING.md` — only then make changes.
2. **Do not commit secrets** (keys, tokens, `.env`). Use keys only via environment variables.
3. **Preserve history:** no `push --force`, no history rewrite without instruction.
4. **Verify changes:** before reporting done, build (`dotnet build Il2CppAssemblyFixer.sln -c Release`
   or `build.ps1`) and run `tests/` / `examples/` where applicable.
5. **Keep docs in sync:** for new features update `README.md` + `docs/` + `CHANGELOG.md` (Unreleased).
6. **Conventions:** Conventional Commits (`feat:`, `fix:`, `docs:`, `chore:` …), one logical change per commit.
7. **When unsure:** stop and ask instead of guessing — especially for deletes, migrations, CI.

## Build and references

- Solution `Il2CppAssemblyFixer.sln`: console tool (`Program.cs`,
  `Il2CppAssemblyFixer.csproj`) + plugin (`MelonPlugin/FixerPlugin.cs`) +
  shared (`Shared/FixerConfig.cs`, `FileLogger.cs`, `Telemetry*.cs`).
- Config: `fixer_config.json`. Telemetry notes: `README_TELEMETRY.md`.
- Never commit `bin/`, `obj/`, or game assemblies.

## Hard rules

- The fixer never mutates the game install in place without an explicit opt-in;
  default to dry-run/report plus fixed copies.
- Telemetry defaults stay off unless the user opts in (see `Shared/TelemetryDefaults.cs`).
- Security: `SECURITY.md` applies.

## Layout

- `Program.cs` — CLI entry. `MelonPlugin/` — plugin entry.
- `Shared/` — config, logging, telemetry. `docs/`, `examples/`, `tests/` — docs and verification.
