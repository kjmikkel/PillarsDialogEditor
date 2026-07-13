# Stale Patch Data Hygiene — Design

**Date:** 2026-07-13
**Gap:** *Stale Patch Data Hygiene* (`Gaps.md`)

## Problem

A saved `.dialogproject` accumulates **stale node-keyed data** — `Translations`,
`NodeComments`, and canvas `Layouts` entries that reference node IDs which no longer
exist in the conversation. Unlike the VO lifecycle work (2026-07-02), which cleans
orphaned `.wem` files, the equivalent *data* rot is untracked and grows silently.

Two confirmed sources in the normal editing flow:

1. **`NodeComments`.** `DeleteNodeCommand` (`DialogEditor.ViewModels/Editing/DeleteNodeCommand.cs`)
   removes the node and its connections but **not** its `_nodeComments` entry. On save,
   `FoldCanvasIntoProject` writes `NodeComments = Canvas.NodeComments` wholesale
   (`MainWindowViewModel.cs:1320`), so a deleted node's translator note persists forever.
2. **`Translations` (non-canvas languages).** The save re-diffs only the canvas language;
   every *other* language's entries are carried over verbatim from the prior patch
   (`MainWindowViewModel.cs:1330-1335`), so a deleted node's German/French/etc. text is
   never dropped.

`Layouts` is mostly self-healing (`GetCurrentLayout` rebuilds from live nodes; the
`WithLayout` doc comment notes it purges deleted-node entries). Only a git *merge*
(`MergeLayout` unions both sides) can introduce a stale layout entry.

Canvas **annotations** are keyed by their own GUID `Id` and are spatial (not
node-attached), so they cannot be stale-by-node and are out of scope.

## Detection model

A referenced node ID is **stale** when it points at a node that no longer exists in the
conversation. Two confidence tiers:

- **Confirmed stale** — the ID is in the patch's own `DeletedNodeIds`. Game-folder-free,
  zero false positives. Catches the dominant leak (deleting a vanilla node).
- **Likely stale** — the ID is absent from the reconstructed **effective node set**
  (`base ∪ AddedNodes − DeletedNodeIds`). Requires an open game folder **and** an opt-in
  toggle. Catches create-then-delete of an *added* node and hand-edited / merge-introduced
  orphans. Flagged with a **version-skew caveat**: a node the installed game version lacks
  but the patch's target version had will look orphaned, so a likely-stale finding may be a
  false positive.

Legitimate `Translations`/`NodeComments` entries on **unmodified vanilla nodes** must never
be flagged. That is precisely why "ID not in the patch's structural sets" is *unsafe* as a
staleness signal (it would flag every translation of an untouched vanilla line) and why the
likely tier requires the real effective node set, not a patch-only shortcut.

## Component 1 — `ProjectStaleDataScanner` (new)

`DialogEditor.ViewModels/Services/ProjectStaleDataScanner.cs`

```csharp
public enum StaleDataKind      { Comment, Translation, Layout }
public enum StaleConfidence    { Confirmed, Likely }

public sealed record StaleDataRow(
    string ConversationName, int NodeId, StaleDataKind Kind,
    string? Language, StaleConfidence Confidence, string Message);

public static class ProjectStaleDataScanner
{
    public static IReadOnlyList<StaleDataRow> Scan(
        DialogProject project,
        Func<string, IReadOnlySet<int>?>? effectiveNodeIds = null);
}
```

- **Confirmed pass** — pure over `project.Patches`, IO-free (mirrors
  `ProjectTextTagScanner`'s purity). For each patch, build the `DeletedNodeIds` set and
  emit a row for:
  - every `NodeComments` key in it (`Kind = Comment`, `Language = null`),
  - every `Translations[lang]` entry whose `NodeId` is in it (`Kind = Translation`,
    `Language = lang`),
  - every `Layouts[conv]` key in it (`Kind = Layout`, `Language = null`).
  All `Confidence = Confirmed`.
- **Likely pass** — runs only when `effectiveNodeIds` is supplied. The delegate maps a
  conversation name → its live node-ID set, or `null` when the conversation can't be
  resolved (missing/unreadable game file) → that conversation is **skipped**, never flagged
  (same skip-on-load-failure discipline as `VoOrphanScanner`). For each referenced ID
  neither present in the effective set nor already reported `Confirmed`, emit a `Likely`
  row of the matching `Kind`.
- Ordering: by conversation (ordinal), then node ID, then kind, then language — stable and
  deterministic for golden-style assertions.

The scanner stays pure and unit-testable: all IO (loading base conversations, applying the
patch, unioning added/deleted, and using the **live canvas snapshot** for the open
conversation) lives in the delegate built by `MainWindowViewModel`.

## Component 2 — Prevention (save-time filtering)

`MainWindowViewModel.FoldCanvasIntoProject` (`MainWindowViewModel.cs:1315`).

At save time the canvas holds the **full effective conversation** (base ∪ added − deleted),
so its live node-ID set is authoritative and game-folder-free. After the patch is built and
before `WithPatch`, filter to that set:

- `NodeComments` → drop keys not in the canvas node-IDs.
- `Translations` (**all** languages, including the carried-over non-canvas ones assembled at
  `MainWindowViewModel.cs:1330-1335`) → drop entries whose `NodeId` is not live.
- `Layouts` → unchanged; `GetCurrentLayout` already rebuilds from live nodes.

This stops rot at the source for any conversation the writer edits and saves, and makes the
reactive prune robust: even if the saved file is pruned while a conversation is open, the
canvas's next save cannot re-introduce a stale comment/translation.

The canvas node-ID set is taken from `Canvas.BuildSnapshot()` (already computed for the
diff on the same code path), so prevention adds no extra snapshot build.

## Component 3 — Surface + prune (Validate Text window)

The stale-data report lives as a new section in the existing **Test ▸ Validate Text…**
window, which already walks every patched conversation across all languages.

`TextTagValidationViewModel` gains:

- `ObservableCollection<StaleDataRowViewModel> StaleRows`, a `StaleSummaryText`, and a
  `HasStaleData` flag, populated by a `ProjectStaleDataScanner` closure re-run inside the
  shared `Refresh`.
- A **"Also check against the game files"** toggle that enables the likely tier. Disabled
  (with an explanatory tooltip) when no game folder is open.

Rendered as a distinct **"Stale data"** section below the existing text-issues list.
Columns: Conversation · Node · Category (Comment / Translation (de) / Layout) · a
Confidence badge (Confirmed / Likely).

Prune, mirroring the orphaned-VO armed two-click cleanup:

- **"Remove stale data"** — armed two-click button that acts on **Confirmed rows only**. It
  rebuilds each affected `ConversationPatch` (pruned `NodeComments` / `Translations`) and
  the `DialogProject.Layouts` map (pruned layout keys), updates `_project`, saves to disk,
  and re-runs the scan.
- Each **Likely** row carries its own individual **"Remove"** button with the version-skew
  caveat, so pruning a possible false-positive is always a deliberate per-item act.

The window's existing three-way dirty guard (`ConfirmScanWithUnsavedChanges`) already gates
opening; the prune operates on the saved `_project`.

### Prune mechanics

Given the rows to remove, group by conversation and rebuild:

- `NodeComments`: a new dictionary omitting the stale keys.
- `Translations`: per language, a new list omitting entries whose `NodeId` is stale for that
  language.
- `Layouts` (on `DialogProject`, not the patch): a new per-conversation map omitting the
  stale keys.

Then `_project = _project.WithPatch(cleaned)…` (and a layout replacement), persist via the
existing save path, and `Refresh()` the window.

## Error handling

Per CLAUDE.md, every caught exception in production code logs via `AppLog.Error/Warn`
(except `OperationCanceledException`). The delegate's per-conversation load failure warns
and skips (as `VoOrphanScanner` does); the scanner core is pure and throws nothing.

## Localisation & UI rules

All new user-visible strings (section header, category labels, confidence badges, toggle
label + tooltip, the two prune buttons + their tooltips, summary/empty-state text) go in
`Strings.axaml`. Every new interactive control (toggle, prune buttons) carries a detailed
`ToolTip` and the paired `AutomationProperties.Name` per the accessibility rules.

## Testing (red/green TDD)

- **`ProjectStaleDataScannerTests`**
  - Confirmed detection for each category (comment, translation-per-language, layout).
  - Likely detection via a stub `effectiveNodeIds` delegate (removed added-node orphan).
  - Null-delegate-for-a-conversation → that conversation is skipped, nothing flagged.
  - A legit translation/comment on an unmodified vanilla node (ID not in any structural set,
    delegate reports it present) is **not** flagged — the key false-positive guard.
  - No `effectiveNodeIds` supplied → only confirmed rows.
  - Ordering is deterministic.
- **Prevention** (MainWindowViewModel suite): deleting a node that carries a comment and
  multi-language translations, then saving, drops all its `NodeComments`/`Translations`
  entries from the saved patch.
- **`TextTagValidationViewModel`**: stale rows populate and refresh; confirmed-bulk prune
  removes only confirmed rows and re-scans; a likely per-row prune removes just that row;
  summary counts; toggle gating.

## Out of scope / deferred

- Delete-time (as opposed to save-time) pruning — the save fold is the clean choke point and
  avoids entangling the undo stack.
- Pruning stale data in conversations that were never re-saved after this feature ships is
  handled by the reactive sweep, not prevention.
- A standalone hygiene window — the Validate Text section is the agreed home.
