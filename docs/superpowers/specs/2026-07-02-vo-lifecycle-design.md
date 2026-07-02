# VO File Lifecycle — Design

**Date:** 2026-07-02
**Status:** Approved by user

## Problem

Nothing ever deletes files from the project's `_vo/` staging folder, and the UI
reports VO presence from `File.Exists` alone. Consequences:

1. **Stale female-VO indication.** A `_fem.wem` on disk makes the detail pane
   claim a female variant even when the node has no female text (observed on
   the user's test node 107).
2. **Orphan accumulation.** Deleting a node, clearing female text, or removing
   a conversation from the project leaves its `.wem` files in `_vo/`. Orphans
   are re-synced to the game folder on every F5 and ship inside every exported
   `.dialogpack`.
3. **Node-ID reuse hazard.** VO files are named `<conversation>_<nodeId>.wem`.
   `NodeIdAllocator` can reuse a deleted node's ID, silently attaching the old
   node's audio to an unrelated new line. An orphan scan cannot catch this case
   because the file "legitimately" matches the new node.

The game directory itself is already safe (F6 and crash-recovery restore remove
synced copies); the dirt lives in `_vo/` and everything derived from it.

## Decisions (user-confirmed)

- **Cleanup policy:** scan + confirm. No silent deletion, no moment-of-change
  prompts. The Validate Voice-Over window lists orphans and offers an explicit,
  confirmed "Clean up…" action.
- **Female VO is intent-driven.** The female variant is only reported when the
  node has female text AND the file exists. A fem file without female text is
  an orphan.
- **Scan scope: whole project.** Every file in `_vo/` is matched against every
  patched conversation, not just the open one.

## Design

### 1. Intent-driven female VO

`VoPathResolver.Check` gains a `hasFemaleText` parameter. `FemaleVariantFound`
(and the fem path in results, including `WithLocalVoFallback`) is only set when
`hasFemaleText && File.Exists(fem)`. Callers pass the node's female-text state
(`NodeViewModel.HasFemaleText` in the detail pane; `FemaleText != ""` on
snapshots elsewhere). The import dialog is already intent-driven
(`destFem = HasFemaleText ? … : null`) and needs no change.

Accepted consequence: a fem file imported before the female text is written is
invisible (and an orphan) until the text exists — matching the game, which will
not use a fem variant without fem text.

### 2. Orphan scan (project-wide), shown in Validate Voice-Over

Compute the **expected set** of `_vo/`-relative paths:

- For every patched conversation in the project: load the vanilla file, apply
  the patch (`ignoreConflicts: true`); for the conversation open on the canvas
  use the live canvas snapshot instead (unsaved edits count).
- For each VO-enabled node (`HasVO` or non-empty `ExternalVO`):
  - `ExternalVO` set → that verbatim relative path + `.wem`.
  - Otherwise → `<chatterPrefix>/<conversation>_<nodeId:0000>.wem`
    (skip nodes whose speaker has no known prefix).
  - Add the `_fem.wem` variant only when the node has female text
    (current-language translation).

**Orphans** = files under `_vo/` (recursive, `*.wem`) not in the expected set.
This covers deleted nodes, cleared female text, and removed conversations.

UI: `VoValidationViewModel` gains an "Orphaned files" section beneath the
missing-VO results, populated by the same Run/Run-again scan. New strings in
`Strings.axaml`; all new controls carry tooltips.

### 3. Clean up — explicit and confirmed

A "Clean up…" button appears when orphans exist. It shows the exact file list,
asks for one confirmation, deletes those files from `_vo/` only, and removes
prefix directories left empty. The game directory is never touched. Recovery
from a mistaken cleanup is re-importing the source audio.

### 4. Node-ID reuse guard

`NodeIdAllocator` skips an otherwise-free ID when `_vo/` contains a file named
for that ID in the current conversation. Prevents the silent audio-inheritance
case the scan cannot detect. Mechanism: the allocator (Core, no file-system
knowledge) accepts an optional `isReserved` predicate; the canvas/VM layer
supplies one that checks `_vo/<prefix>/<conversation>_<id:0000>.wem` for any
known prefix when a project with a `_vo/` folder is open.

### 5. Error handling

Per project convention: all caught exceptions logged via `AppLog`
(`OperationCanceledException` swallowed silently); scan failures surface in the
window's summary line; cleanup reports per-file failures and continues.

### 6. Testing (TDD, red/green per behavior)

- Resolver: fem variant reported only with female text; fallback respects it.
- Scan: orphan detected for (a) deleted node, (b) cleared female text,
  (c) conversation removed from project; no false positive for referenced
  files, including `ExternalVO`-named ones and the live canvas state.
- Cleanup: deletes exactly the confirmed list; prunes empty dirs; leaves
  non-orphans and the game folder untouched.
- Allocator: skips IDs owning `_vo/` files; still allocates normally otherwise.
