# Diff Before/After Text Detail — Design

**Date:** 2026-05-31
**Status:** Approved (brainstorming complete; ready for implementation plan)

## Goal

Complete the diff-viewing feature: when a writer selects a node on the read-only
diff canvas, show that node's **before/after text** in a dedicated panel, with
the changed portions highlighted inline. This closes the deferred item recorded
in `Gaps.md`:

> "Before/after text detail for a selected node is not yet implemented (deferred
> — the canvas tinting is the priority)."

## Background

The diff window (`DiffWindow.axaml` / `DiffViewModel`) already:

- Lists changed conversations with +/~/− counts.
- Renders a selected conversation on a read-only canvas (`DiffCanvas`, a
  `ConversationViewModel`) with per-node colour tinting (green = added,
  amber = changed, red = removed), injecting ghost nodes for removed nodes from
  the left (old) project's reconstruction.

Two existing pieces are reused rather than rebuilt:

- **`ReconstructConversation(name, project, provider)`** in `DiffViewModel`
  rebuilds the effective `Conversation` (game base + patch + translations) for
  either endpoint. The left reconstruction already happens inside
  `BuildDiffCanvas` to ghost removed nodes.
- **`TextDiff.Diff(mine, theirs)`** (`DialogEditor.Patch/GitConflict/TextDiff.cs`)
  returns `DiffSpan`s (`Common` / `MineOnly` / `TheirsOnly`).
  `GitConflictResolutionWindow.UpdateDiff` already renders these spans as
  coloured `Run`s in an `InlineCollection`. The new panel mirrors that exact
  pattern, with the two sides labelled **Before** and **After** instead of
  *Mine* / *Theirs*.

`ConversationViewModel.SelectedNode` is an `[ObservableProperty]`, so canvas
clicks can be observed via `PropertyChanged` without touching the Nodify canvas
controls.

## Design

### Component 1 — `NodeDiffDetailViewModel` (new)

Location: `DialogEditor.ViewModels/ViewModels/NodeDiffDetailViewModel.cs`

Holds the *decision logic* for one selected node so it can be unit-tested in
isolation. It does **not** render inline runs (that is view-layer glue).

Constructed from: node id, a `Kind`, and the before/after text for the two text
fields. Exposes:

- `int NodeId`
- `DiffStatus Kind` — reuses the existing `DiffStatus` enum
  (`Added` / `Changed` / `Removed`); `Unchanged` is never produced here.
- `string DefaultBefore`, `string DefaultAfter`
- `bool HasFemaleRow` — `true` when **either** side has non-empty female text.
- `string FemaleBefore`, `string FemaleAfter`
- `bool IsStructuralOnly` — `true` only for `Changed` nodes whose Default **and**
  Female text are identical on both sides (the change was conditions / scripts /
  speaker / flags only). Drives a localized "no text change — structural only"
  hint instead of two identical lines.

Placeholder rules (localized strings, not inline literals):

- **Added** node: `DefaultBefore` / `FemaleBefore` are the placeholder
  `Diff_Detail_NodeAdded` ("(node added — no previous text)"); the *after* side
  holds the real text.
- **Removed** node: `DefaultAfter` / `FemaleAfter` are the placeholder
  `Diff_Detail_NodeRemoved` ("(node removed)"); the *before* side holds the real
  text.
- **Changed** node: both sides hold real text.

`HasFemaleRow` is evaluated against the *real* text only — a placeholder side
does not by itself force the female row visible.

### Component 2 — `DiffViewModel` changes

- **Cache text maps.** In `BuildDiffCanvas`, reconstruct both the left and right
  conversations once for the selected conversation and cache
  `Dictionary<int,(string Default, string Female)>` for each side
  (`_leftTextById`, `_rightTextById`). The left reconstruction already occurs for
  ghosting; this hoists it so it runs whenever a selection exists (not only when
  removed nodes are present) and is reused for both ghosting and the detail panel.
  Text is read from the reconstructed `Conversation.Strings.Get(nodeId)`
  (`DefaultText` / `FemaleText`).
- **Observe selection.** After building the canvas `vm`, subscribe to
  `vm.PropertyChanged`; when `SelectedNode` changes, build `SelectedNodeDetail`
  from the cached maps and the node's `DiffStatus`. Unsubscribe / replace the
  handler when the canvas is rebuilt to avoid leaks across endpoint changes.
- **Expose** `[ObservableProperty] NodeDiffDetailViewModel? _selectedNodeDetail`.
  Cleared (`null`) when: the canvas rebuilds, the canvas selection clears, or the
  selected conversation changes.
- **Applied-Preview scope cut.** The detail panel is active in
  `CanvasMode.Changes` only. In `CanvasMode.AppliedPreview` the before/after
  semantics differ (working copy → projected), so `SelectedNodeDetail` is kept
  `null` and the panel stays hidden; node tinting still conveys the preview.
  This is an intentional limitation (see below).

### Component 3 — `DiffWindow.axaml` + code-behind

- A `Border` docked `DockPanel.Dock="Bottom"` in the right-hand canvas column,
  placed **above** the existing apply bar and status bar.
  `IsVisible` is bound to `SelectedNodeDetail` via the existing `IsNotNull`
  converter.
- Contents:
  - Header `TextBlock`: `Node {NodeId}` (localized format string).
  - A "Default text" row with two named `TextBlock`s: `DefaultBeforeText`,
    `DefaultAfterText`, each prefixed by localized **Before** / **After** labels.
  - A "Female text" row (`DefaultBefore/After` analogues `FemaleBeforeText`,
    `FemaleAfterText`), the whole row's `IsVisible` bound to `HasFemaleRow`.
  - A structural-only hint `TextBlock`, `IsVisible` bound to `IsStructuralOnly`.
  - A `ToolTip.Tip` on the panel explaining what before/after means in this
    context.
- Code-behind `UpdateDetail()` — invoked when `SelectedNodeDetail` changes —
  fills each visible `TextBlock.Inlines` from `TextDiff.Diff(before, after)`,
  reusing the Common / Mine / Theirs brush scheme from
  `GitConflictResolutionWindow` (Common = unchanged, "Mine" brush = before-only
  text, "Theirs" brush = after-only text). When `IsStructuralOnly` is true the
  text rows are not populated (the hint is shown instead).

### Component 4 — Localization

New keys in `DialogEditor.Avalonia/Resources/Strings.axaml`:

- `Diff_Detail_Header` — `"Node {0}"` (format)
- `Diff_Detail_BeforeLabel` — `"Before"`
- `Diff_Detail_AfterLabel` — `"After"`
- `Diff_Detail_DefaultTextLabel` — `"Default text"`
- `Diff_Detail_FemaleTextLabel` — `"Female text"`
- `Diff_Detail_NodeAdded` — `"(node added — no previous text)"`
- `Diff_Detail_NodeRemoved` — `"(node removed)"`
- `Diff_Detail_StructuralOnly` — `"No text change — only structural fields
  (conditions, scripts, or speaker) differ."`
- `ToolTip_Diff_Detail` — explains the before/after panel in plain language.

## Data flow

```
user clicks node on DiffCanvas
  → ConversationViewModel.SelectedNode changes
  → DiffViewModel handler reads node id + DiffStatus
  → look up id in _leftTextById / _rightTextById
  → build NodeDiffDetailViewModel (applies placeholder / structural-only rules)
  → DiffViewModel.SelectedNodeDetail = vm
  → DiffWindow code-behind UpdateDetail() renders TextDiff spans into Inlines
```

## Error handling

- Text-map construction runs inside the existing `try`/`catch` in
  `BuildDiffCanvas`; failures are logged via `AppLog.Warn` (consistent with the
  surrounding code) and leave `SelectedNodeDetail` null rather than throwing.
- Missing string entries fall back to the existing `Node_TextUnavailable`
  placeholder already used by `NodeViewModel`.
- The whole canvas (and therefore the panel) only exists when a game-data
  `provider` is present; no separate no-game-folder path is needed.

## Testing (red/green TDD)

- **`NodeDiffDetailViewModelTests`** (new):
  - Changed node populates both before and after for Default text.
  - Added node → before is the `NodeAdded` placeholder; after holds text.
  - Removed node → after is the `NodeRemoved` placeholder; before holds text.
  - Female row hidden when both sides' female text empty; shown when either side
    has female text.
  - `IsStructuralOnly` true when a Changed node's Default and Female match on both
    sides; false when any text differs.
- **`DiffViewModelTests`** (extend existing, using the established provider stub):
  - Selecting a node on `DiffCanvas` populates `SelectedNodeDetail` with the
    matching id and before/after text.
  - Rebuilding the canvas / clearing canvas selection nulls `SelectedNodeDetail`.
  - In `AppliedPreview` mode selecting a node leaves `SelectedNodeDetail` null.
- **`DiffWindowTests`** (extend existing headless tests):
  - Panel hidden when nothing is selected.
  - Panel visible after a node is selected on the canvas.

## Intentional limitations / deferred follow-ups

- **Applied-Preview has no detail panel.** Hidden by design (different
  before/after semantics); node tinting carries the preview.
- **Structural changes are summarised, not shown.** A Changed node whose only
  differences are conditions / scripts / speaker / flags shows the
  `StructuralOnly` hint rather than a field-by-field structural diff. *Deferred
  idea (revisit):* an optional expander for power users who want the full
  structural before/after — kept collapsed by default to avoid clutter for the
  majority. Confirmed acceptable during brainstorming.
- **Single language (deferred feature — wanted later).** Before/after
  default/female text uses the diff window's current `_language`. Showing all
  changed languages at once is **not** built now: in practice a single edit
  rarely touches more than one language, and a reviewer can switch the diff
  window's language to double-check the others. But this is a genuine future
  feature, not a permanent exclusion — reviewers will eventually want every
  changed language surfaced together (if only for completeness), so it is
  recorded here to be picked up later rather than dropped.

## Out of scope (YAGNI)

- Editing from the panel (the diff canvas is read-only).
- Structural field diffs inline (see deferred follow-up).
