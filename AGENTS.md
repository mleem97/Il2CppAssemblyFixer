AGENTS
======

Purpose
-------
Commit message conventions, changelog format, and release workflow for this repository.

Boundaries
----------
- Do not rewrite repository history, move tags, or force-push unless the user explicitly requests it.
- Do not commit secrets, telemetry credentials, local paths, build artifacts, or generated release ZIPs.
- Do not change public release behavior, telemetry defaults, or MelonLoader compatibility without documenting the reason in the PR.
- Do not bypass CI or merge failing changes without user confirmation.
- When a rule conflicts with a direct user request, ask for confirmation before proceeding.

1) Commit messages
------------------
Commits should follow the Conventional Commits specification unless the user explicitly requests a different format:
https://www.conventionalcommits.org/en/v1.0.0/

Examples:
  feat(detection): add game-path.txt config override
  fix(dnlib): follow TypeSpec wrappers in reference counting
  chore: remove one-off scripts
  ci: upgrade release action to v2

Breaking changes require the `!` marker or `BREAKING CHANGE:` in the body (triggers a MAJOR bump).

2) Semantic versioning
----------------------
SemVer: MAJOR.MINOR.PATCH

  feat        → MINOR
  fix         → PATCH
  BREAKING    → MAJOR
  chore/docs/ci/refactor/test → no version bump

3) Changelog format
-------------------
Use Keep a Changelog (https://keepachangelog.com) in CHANGELOG.md.
Keep an `[Unreleased]` section at the top.
When cutting a release, move Unreleased entries into `## [X.Y.Z] - YYYY-MM-DD` and tag the commit.
Groups: Added, Changed, Fixed, Removed, Security.

4) Git hooks
------------
A commit-msg hook lives in `.githooks/`. Enable it locally:

  git config core.hooksPath .githooks

5) Release workflow
-------------------
1. Fill in CHANGELOG.md Unreleased section.
2. Bump version in `MelonPlugin/Il2CppAssemblyFixerPlugin.csproj` (MelonInfo attribute).
3. Commit: `chore(release): prepare v X.Y.Z`
4. Tag: `git tag vX.Y.Z`
5. Push branch and tag: `git push && git push --tags`
6. CI builds and publishes the GitHub Release automatically.
