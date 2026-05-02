AGENTS
======

Purpose
-------
This document defines the repository agent rules: commit message conventions, changelog format, and semantic-versioning/release workflow.

1) Commit messages
------------------
- All commits MUST follow the Conventional Commits specification: https://www.conventionalcommits.org/en/v1.0.0/
- Example summary lines:
  - `feat(parser): add support for xyz`
  - `fix(dnlib): avoid deleting referenced types`
  - `chore: update build script`
- Breaking changes MUST include the `!` marker or `BREAKING CHANGE:` in the body. These trigger a MAJOR version bump.

2) Semantic versioning
----------------------
- Versioning follows Semantic Versioning (SemVer): MAJOR.MINOR.PATCH
- Map Conventional Commit types onto SemVer:
  - `feat` → MINOR
  - `fix` → PATCH
  - BREAKING CHANGE / `!` → MAJOR
  - `chore`, `docs`, `style`, `refactor`, `test`, `ci`, `build` → do not affect version unless explicitly bumped

3) Changelog format
-------------------
- Use "Keep a Changelog" format in `CHANGELOG.md`.
- Maintain an `Unreleased` section at the top. When cutting a release, move the Unreleased entries into a new heading `## [X.Y.Z] - YYYY-MM-DD` and tag the commit.
- Keep entries grouped by type: Added, Changed, Fixed, Removed, Security.

4) Enforcement (recommended)
---------------------------
- This repo contains a sample commit-msg hook in `.githooks/commit-msg` (bash) and
  `.githooks/commit-msg.ps1` (PowerShell). To enable locally run:

```powershell
git config core.hooksPath .githooks
```

This causes Git to run the provided hook which rejects messages that don't match Conventional Commits.

5) Release workflow (human-reviewed)
-----------------------------------
1. Ensure all commits since the previous release follow Conventional Commits.
2. Update `CHANGELOG.md` Unreleased section with human-readable release notes.
3. Bump the version (create a tag `vX.Y.Z` on the commit with changelog changes).
4. Push tags and create a GitHub Release using the tag and the changelog entry as release notes.

NOTE: The repository owner requested a potentially destructive historical rewrite workflow (relabeling/retagging every past release and force-pushing). That action rewrites history and must be explicitly authorized. It will require coordination with all collaborators and is NOT performed automatically by hooks in this repo.

6) Automation suggestions
-------------------------
- Add CI checks to ensure commit messages conform (e.g., a GitHub Actions job using a conventional-commit linter).
- Add a release action that reads `CHANGELOG.md` to create GitHub Releases from the topmost unreleased section.

7) Contact
----------
If you want me to proceed with history rewriting and full release recreation, reply with explicit consent and the GitHub auth method to use. Otherwise I will not force-push or delete releases.
