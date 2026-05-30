# Selective Apply (Spec 2) — Design

**Date:** 2026-05-30
**Status:** Approved (brainstorming)

## Context

The read-only **diff viewer (Spec 1)** shipped on `main`: it loads two endpoints, computes a per-conversation `ProjectDiff`, and renders the selected conversation on a tinted canvas. Spec 1 was the "diff viewing" half of the original "diff + full apply" request; **selective apply is the deferred second half**.

This spec lets the user, from the diff window, **cherry-pick individual node changes** from the read-only endpoint into their working-copy `.dialogproject`. It builds directly on Spec 1's diff model, endpoint loading, and canvas tinting.

See `docs/superpowers/specs/2026-05-30-diff-viewer-design.md` (Spec 1) and `docs/superpowers/plans/2026-05-30-diff-viewer.md`.

## Decisions locked during brainstorming

| Topic | Decision |
|---|---|
| **Direction** | Only the working-copy `.dialogproject` is writable. Apply is enabled **only when the working copy is one of the two endpoints**, and pulls selected changes from the *other* (source) endpoint into it. When two git refs are compared, apply is disabled with an explanatory tooltip. |
| **Granularity** | **Per node**, grouped under each conversation, with a "select all in conversation" tri-state checkbox. True cherry-pick. |
| **Dangling links** | **Warn, but allow.** Detect links that would point at a node not present after apply; show a non-blocking warning listing them. The user may apply anyway. Automatic dependency-pulling is out of scope. |
| **Engine** | A **dedicated `NodeApplyBuilder`** in `DialogEditor.Patch`, alongside `MergeBuilder` (not merged into it). MergeBuilder is field/conflict-oriented; apply is node-oriented. They share only small plumbing idioms. |
| **Save guard** | Apply requires a clean (saved) working copy so memory and disk agree. If the editor is dirty when Apply is clicked, **prompt to save → apply**; cancel aborts. |
| **Post-apply state** | **Write the `.dialogproject` to disk and keep the live editor in sync (clean, not dirty), with a single-step undo.** |
| **Applied preview** | A **segmented toggle** above the canvas: *Changes* (existing full-diff view) and *Applied preview* (reconstructs the projected result from the current selection and tints only the nodes the selection would change). Updates live as checkboxes change. |

## Key insight

A `DialogProject` is essentially a dictionary of per-conversation `ConversationPatch` objects. `ProjectDiff.Signatures()` already groups each node's *full contribution* to a patch into four buckets — `AddedNodes`, `ModifiedNodes`, `DeletedNodeIds`, and per-language `Translations`. **Applying a node therefore means copying those four buckets for that node id from the source patch into the target patch** — a coarser unit than `MergeBuilder`'s field-level overlay. Because the engine is pure and fast, both the apply result and the live "applied preview" reconstruction can re-run on every checkbox toggle.

Undo is **conversation-scoped** in this app (each `ConversationViewModel` owns an `UndoRedoStack` of `IEditCommand`). A selective apply spans multiple conversations, so it is a **project-level** operation — it does not fit the per-canvas Ctrl+Z stack. Project-level changes already flow through `SetProject(...)` (e.g. conflict-merge does `SetProject(merged)`); selective apply uses the same path plus a dedicated single-step revert.

## Scope

**In scope:**
- A pure `NodeApplyBuilder.Apply(target, source, selection)` that returns a new `DialogProject`.
- A pure `NodeLinkAnalyzer` that reports dangling links in a projected result.
- Per-node checkable selection in the diff window (two-level tree), with `CanApply` gating.
- Editor integration: save-guard prompt, write-to-disk + in-sync editor, single-step "Undo apply".
- Segmented canvas toggle with a live, selection-aware "applied preview".

**Out of scope (YAGNI / later):**
- Automatic dependency-pulling when a selection would dangle (warn-only in v1).
- Before/after node-text detail in the warning or canvas (already deferred from Spec 1 Task 9).
- Multi-step apply history (single revert only).
- Applying *into* a git ref (only the working copy is writable).

## Architecture & components

Pure logic lives in testable core units; the UI changes attach to the existing `DiffWindow` and `MainWindowViewModel`. Each unit has one responsibility.

### `NodeApplyBuilder` (pure, `DialogEditor.Patch/Diff/`)

```csharp
public static DialogProject Apply(
    DialogProject target,                    // the working copy (writable side)
    DialogProject source,                    // the other endpoint
    IReadOnlyList<NodeSelection> selected);   // (ConversationName, NodeId) pairs

public readonly record struct NodeSelection(string ConversationName, int NodeId);
```

- **Canonical rule (direction-agnostic):** for each selected `(conv, nodeId)`, make the target patch's contribution for that node **identical to the source's**. This unifies all cases without depending on the diff's left→right labels (which are presentation only):
  - source has the node in `AddedNodes` / `ModifiedNodes` / `DeletedNodeIds` / per-language `Translations` → set/replace the same bucket(s) in a clone of `target.Patches[conv]`;
  - source has **no contribution** for the node (it equals the base game file on the source side) → **remove** the node from the target patch entirely (drop from Added/Modified/Deleted/Translations), i.e. revert it to base.
- Because the labels are not load-bearing, the same rule correctly handles every list item: an *Added* item adopts source's added node, a *Modified* item adopts source's modification, a *Removed* item adopts source's "no contribution" (reverting/removing it in the target). Which side is older/newer is irrelevant.
- Returns a new `DialogProject` (records are immutable); never mutates inputs. An empty selection returns `target` unchanged.
- Reuses MergeBuilder's `ById` / `ConversationPatch with { ... }` *idiom*, not its conflict API.

### `NodeLinkAnalyzer` (pure, `DialogEditor.Patch/Diff/`)

`Analyze(DialogProject projectedResult, IGameDataProvider? provider) → IReadOnlyList<DanglingLink>` where `DanglingLink` is `(string Conversation, int FromNode, int ToNode)`. Finds links in the projected result whose target node will not exist after apply. Independently testable; the VM only formats its output for the warning panel.

### `DiffViewModel` (ViewModels)

- **Selection model:** the flat `Changes` list becomes a two-level checkable structure:
  - `ConversationChangeViewModel` — wraps a `ConversationChange`; expandable; **tri-state** checkbox that rolls its node children up/down.
  - `NodeChangeViewModel` — one per Added/Removed/Modified node id; carries kind + `IsSelected`. Toggling also feeds the applied-preview tinting.
- **`CanApply`** — true iff exactly one endpoint is `WorkingCopy`, the other endpoint differs, and ≥1 node is selected. Drives the Apply button's enabled state and tooltip.
- **`CanvasMode`** enum (`Changes` | `AppliedPreview`) — bound to the segmented toggle; changing it re-runs `BuildDiffCanvas`.
- **`BuildDiffCanvas`** gains a branch:
  - *Changes* — existing behaviour (reconstruct the right endpoint, tint by the full diff sets).
  - *Applied preview* — reconstruct from `NodeApplyBuilder.Apply(workingCopy, source, selectedForThisConv)` and tint by **`diff(workingCopy, projectedResult)`** — i.e. green/orange/red describe what changes *in the working copy* (added / altered / removed by the apply), not the original left→right diff. Nodes the selection does not touch are neutral. Re-runs on checkbox toggle when this mode is active.
- **`ApplyCommand`** — builds `NodeSelection[]` from the checked nodes → `NodeApplyBuilder.Apply` → `NodeLinkAnalyzer` → if dangling links, populate the (non-blocking) warning → raise **`CommitApply(DialogProject result)`** for the host to persist.

### Editor integration — `MainWindowViewModel` & the detached-window bridge

`DiffWindow` is opened detached (`new DiffWindow(diffVm).Show()`), so we wire a host callback in the same spirit as the existing `ShowGitConflictResolution`:

- In `MainWindow.CompareVersions_Click`, set `diffVm.CommitApply = applied => mainVm.ApplyFromDiff(applied)`.
- New `MainWindowViewModel.ApplyFromDiff(DialogProject applied)`:
  1. **Save guard:** if `IsModified`, raise the existing unsaved-changes prompt; on cancel, abort (no write).
  2. Capture `_preApplyProject = _project` (the just-saved, on-disk state).
  3. `SetProject(applied)` then `SaveProject()` → disk and memory in sync, not dirty.
  4. Expose **`UndoApplyCommand`**: `SetProject(_preApplyProject)` + save — a dedicated single-step revert (explicitly *not* the conversation-scoped Ctrl+Z stack, which cannot represent a cross-conversation change).

### UI — `DiffWindow.axaml`

- Replace the flat `ListBox` item template with the expandable, checkable two-level tree (node rows reuse the existing `+ / ~ / −` colour coding).
- Add a **segmented toggle** above the canvas: *Changes* / *Applied preview*, each with a tooltip describing what it shows.
- Add a bottom-docked **apply bar**: an Apply button, a checked-count label, an **Undo apply** button, and a collapsible dangling-link warning panel.
  - Apply tooltip (enabled): "Applies the checked changes into your working-copy `.dialogproject`. The working copy must be one of the two compared versions; the changes are written into it and saved."
  - Apply tooltip (disabled): explains that apply requires the working copy to be one of the two endpoints, and a saved (non-dirty) editor.
- All new strings live in resource dictionaries / `.resx` (localisation rule); every new control carries a detailed `ToolTip` (UI/UX rule).

## Data flow

1. User compares **working copy** against a **git ref** (or ref against working copy) → Spec 1 produces `Changes`.
2. User expands conversations and checks individual node changes; the tri-state conversation checkboxes roll up.
3. (Optional) User switches the canvas to **Applied preview** to see the projected working copy for the selected conversation, tinted by their current selection; toggling checkboxes updates it live.
4. User clicks **Apply** (enabled only when `CanApply`).
5. `ApplyCommand` projects the result via `NodeApplyBuilder`, runs `NodeLinkAnalyzer`, shows any dangling-link warning, and raises `CommitApply(result)`.
6. `MainWindowViewModel.ApplyFromDiff` runs the save-guard, snapshots the pre-apply project, `SetProject(result)`, and saves to disk.
7. User may **Undo apply** once to restore the snapshot and re-save.

## Error handling

- Every `catch` logs via `AppLog.Error(...)` / `AppLog.Warn(...)` before/after any status update (per CLAUDE.md); `OperationCanceledException` is swallowed silently. No bare `catch {}`.
- Save/serialize failures during `ApplyFromDiff` surface a localized status message and abort without leaving a partial or dirty state.
- `NodeApplyBuilder` tolerates a missing source/target patch (treats as empty) rather than throwing.

## Testing (strict red/green TDD)

Tests mirror structure in `DialogEditor.Tests`.

- **`NodeApplyBuilder`** — added node, modified node, removed node, translation-only change, multi-conversation selection, empty selection (no-op / returns target), input immutability, missing source/target patch.
- **`NodeLinkAnalyzer`** — dangling link detected when a selected node links to an unselected/absent node; clean case yields none; removed-but-still-linked case.
- **`DiffViewModel`** — `CanApply` across endpoint combinations (working-vs-ref, ref-vs-working, ref-vs-ref disabled, empty selection disabled); tri-state roll-up; `CanvasMode` switching rebuilds the canvas; applied-preview tinting reflects the selection.
- **`MainWindowViewModel.ApplyFromDiff`** — save-guard prompt on dirty (proceed vs cancel), pre-apply snapshot, `SetProject` + save leaves a clean state, `UndoApplyCommand` restores the snapshot.

## Build sequence

1. `NodeApplyBuilder` (pure) + tests.
2. `NodeLinkAnalyzer` (pure) + tests.
3. `DiffViewModel` selection model, `CanApply`, `CommitApply` + tests.
4. `MainWindowViewModel.ApplyFromDiff` + save-guard + `UndoApplyCommand` + tests.
5. `DiffViewModel` applied-preview branch (`CanvasMode`) + tests.
6. `DiffWindow.axaml` UI: checkable tree, segmented toggle, apply bar, warning panel, resources, tooltips.
7. `MainWindow` wiring of `CommitApply`.
