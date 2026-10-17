---
title: Trail setup and owner-only guard for agent-context paths
author: Mikkel
date: 2026-10-17
branch: feat/230-trail-workflow
cost: { minutes: 4, tokens: 75996 }
touches:
  - ".github/CODEOWNERS"
  - ".github/scripts/trail-guard.sh"
  - ".github/scripts/trail-guard.test.sh"
  - ".github/workflows/guard.yml"
  - CLAUDE.md
---

## Decided
- Trail wired with `trail init --no-agents --no-global` (repo files only; the user-level skill/hooks were already installed).
- Ported the owner-only guard from the Dracula-Night-by-Night repo: `.github/scripts/trail-guard.sh` + `.test.sh`, `.github/workflows/guard.yml` (pull_request_target, never checks out PR code), `.github/CODEOWNERS`. Repo-specific test cases cover `.claude/agents/` and `.claude/skills/`, which are tracked here.
- `.claude/settings.local.json` is no longer tracked: it is per-machine (allowlist, trail statusline) and had local paths in a public repo.

## Tried and ruled out
- Committing trail's statusline/allowlist via the tracked settings.local.json — `trail init` writes there, so it would have shipped machine-specific config.

## Watch out
- The guard runs from main, so changes to it only take effect on the PR after the one that merges them. Mark `guard / trail-owner-only` required after merge.
- Changing the protected set = edit trail-guard.sh, its test file and CODEOWNERS together.
- `graft/` at the root is a stale cache from trail's old name; ignored, safe to delete locally.
