# Diff Viewer (read-only) — Design

**Date:** 2026-05-30
**Status:** Approved (brainstorming)

## Context

`.dialogproject` files are version-controlled in Git, but there is no way to see *what changed* in a conversation between two points in history without reading raw JSON diffs. This is the "diff viewing" half of the Version Control Integration gap (`Gaps.md`).

"Diff viewing + full apply" is large, so it was **decomposed** during brainstorming:

- **Spec 1 (this document): read-only diff viewer.** Produces the diff model and presents it.
- **Spec 2 (later): selective apply.** Builds on Spec 1 to cherry-pick changes into the working copy; reuses the `MergeBuilder`-style overlay + undo. Out of scope here.

Full apply remains the goal; it is sequenced after the viewer because it depends entirely on the viewer's diff model.

### Decisions locked during brainstorming
- **Presentation:** a **changed-conversations list** (with +/~/− counts) that drills into a **canvas overlay** of the selected conversation.
- **Endpoints:** compare any **two endpoints**, where each endpoint is either the **working copy** (the `.dialogproject` on disk) or a **git ref** (branch/commit). Supports working-copy-vs-ref and ref-vs-ref.
- **Scope:** read-only viewer only (no mutation; apply is Spec 2).
- **Canvas:** reuse the existing canvas in a read-only diff mode (add a `DiffStatus` to `NodeViewModel`) rather than duplicating the Nodify graph rendering.
- **Git access:** shell out to `git` (no new dependency); the editor currently has no git interaction, so this introduces the first.

## Key insight

A `.dialogproject` stores **patches** (per-conversation diffs vs. game files), and a patch already encodes added/deleted/modified nodes and translations. So the **changed-conversations list and counts are a value-aware diff of two projects' patches** — no game folder required, reusing the comparison approach from `GitMergeAnalyzer`. The game folder is only needed to **render the full conversation graph** on the canvas (reconstructing nodes from base game files + patch via the existing `PatchApplier` path). The list works anywhere; the canvas overlay requires a game folder open.

## Scope

**In scope:**
- Loading a `DialogProject` from an endpoint (working-copy file, or `git show <ref>:<path>`).
- Computing a per-conversation change set between two projects.
- A diff window: two endpoint pickers, a changed-conversations list with counts, a read-only canvas overlay with +/~/− tinting, and a before/after detail for a selected node.

**Out of scope (later / other gaps):**
- Selective apply / cherry-pick (Spec 2).
- Branch/history navigation (browsing log, switching branches).
- Editing in the diff view.

## Architecture & components

Pure diff logic lives in testable core units; the UI is a `DiffWindow`. Each unit has one responsibility.

- **`ProjectVersionLoader`** (`DialogEditor.Patch/Diff/` or similar) — resolves an *endpoint* to a `DialogProject`.
  - Working copy → read the file on disk → `DialogProjectSerializer.Deserialize`.
  - Git ref → run `git show <ref>:<relpath>` (via a small injectable `IGitRunner`) → deserialize.
  - Depends on: `DialogProjectSerializer`, `IGitRunner`. Testable with a fake runner; no real git in unit tests.

- **`ProjectDiff`** (pure, core) — `Diff(DialogProject a, DialogProject b) → IReadOnlyList<ConversationChange>`.
  - For each conversation present in either side, compares the effective node sets derived from the patches: nodes added (in b not a), removed (in a not b), modified (present in both but differing — value-aware, mirroring `GitMergeAnalyzer`'s field/translation comparison).
  - `ConversationChange { string Name; IReadOnlyList<int> Added; Removed; Modified; }` with `AddedCount`/`ModifiedCount`/`RemovedCount` convenience.
  - No dependency on game files. Heavy unit tests.

- **`DiffViewModel`** (`DialogEditor.ViewModels`) — orchestrates the window.
  - Two endpoint selectors. Each lists **Working copy**, local branches, and recent commits (e.g. last N from `git log`), sourced via `IGitRunner`.
  - On endpoint change: load both via `ProjectVersionLoader`, run `ProjectDiff`, populate the changed-conversations list (name + counts, sorted).
  - `Selected` conversation → raises a request to render the canvas overlay.
  - Headless-testable with fakes.

- **`DiffWindow`** (`DialogEditor.Avalonia`) — Layout B: endpoint pickers (top), changed list (left), canvas area (right). App icon, tooltips on every control, localized strings, `AppLog` on errors, public parameterless ctor (AVLN3000 guard).

- **Canvas overlay** — reuse the existing canvas:
  - Add `DiffStatus { Unchanged, Added, Removed, Changed }` to `NodeViewModel` (default `Unchanged`) and a read-only flag on the canvas so diff mode disables editing/undo.
  - A new converter (`DiffStatusToBrushConverter`) tints node borders/background green/amber/red; removed nodes are shown ghosted.
  - The diff window hosts the existing canvas view bound to a conversation reconstructed for the "new" endpoint, with `DiffStatus` set per node from the `ConversationChange`. (Removed nodes are injected from the "old" side so they appear ghosted.)

- **Before/after node detail** — selecting a `Changed` node shows old-vs-new field/text values, reusing the resolution dialog's word-level `TextDiff` highlighting.

## Data flow

pick two endpoints → `ProjectVersionLoader` ×2 → `ProjectDiff` → changed-conversations list → select a conversation → reconstruct both sides' conversation (needs game folder) → render canvas with `DiffStatus` tinting → select a changed node → before/after detail.

## Error handling

- Not a git repo / `git` not on PATH / unknown ref / file not tracked at that ref → clear localized status in the diff window; no crash. All caught exceptions logged via `AppLog` (except `OperationCanceledException`).
- Canvas overlay requires a game folder: when none is open, the changed list still works; the canvas area shows a localized hint instead.
- Endpoint deserialization failure (e.g. that ref's file is itself git-conflicted/invalid) → status message naming the endpoint; the other endpoint and the list degrade gracefully.

## Testing

- `ProjectDiff` — pure unit tests: added/removed/modified detection, value-aware "modified" (same node, differing field/translation), conversation only on one side, identical projects → no changes.
- `ProjectVersionLoader` — unit tests with a fake `IGitRunner`: working-copy read, `git show` ref read, invalid ref → error result.
- `DiffViewModel` — headless tests: endpoint selection drives the changed list; counts correct; selection raises render request.
- `DiffWindow` — headless smoke tests (list renders, no throw); `DiffStatusToBrushConverter` unit test.

## Staging (for the implementation plan)

1. **Backend:** `ProjectVersionLoader` + `IGitRunner` + `ProjectDiff` (pure, fully tested).
2. **List UI:** `DiffViewModel` + `DiffWindow` with endpoint pickers and the changed-conversations list (works without a game folder).
3. **Canvas overlay:** `DiffStatus` on `NodeViewModel`, converter, read-only diff mode, before/after detail.

## Follow-ups

- **Spec 2 — selective apply:** check changes in the diff and apply them into the working-copy `.dialogproject` (reusing `MergeBuilder`-style overlay + undo).
- Recent-commits list depth and ref-picker polish can be tuned after first use.
