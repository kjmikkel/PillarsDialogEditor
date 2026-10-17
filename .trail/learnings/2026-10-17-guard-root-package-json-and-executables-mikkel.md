---
title: "Guard: root package.json and executables at any depth"
author: Mikkel
date: 2026-10-17
branch: feat/230-trail-workflow
cost: { minutes: 7, tokens: 20930 }
touches:
  - ".github/CODEOWNERS"
  - ".github/scripts/trail-guard.sh"
  - ".github/scripts/trail-guard.test.sh"
  - CLAUDE.md
---

## Decided
- Guard also blocks non-owner changes to the root `package.json` and to PATHEXT executables (.cmd .exe .js …) at any depth, not just the root.
- Fixed in the guard, not in `.trail/hook.cjs`: `trail init` regenerates hook.cjs, so a local patch there would be silently reverted.

## Tried and ruled out
- Root-only executable blocking: the hook's cwd is wherever the session started, and cmd.exe searches cwd before PATH, so a subdirectory `npm.cmd` would run.

## Watch out
- Reproduced: a root package.json named `@trailhq/trail-code` with `"exports": {"./package.json": "./evil/package.json"}` makes `require.resolve(..., {paths:[repo]})` return evil/package.json; hook.cjs then imports evil/dist/claude/hooks.js, and a high fake version wins its version pick.
- Same two gaps exist in kjmikkel/Dracula-Night-by-Night's guard and in upstream trail's hook.cjs template.
- Nested `.js` files now need the owner; tests that only meant "a dist/ file" use `.json`.
