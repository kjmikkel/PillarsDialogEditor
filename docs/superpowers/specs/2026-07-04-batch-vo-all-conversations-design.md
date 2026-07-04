# Project-Wide Batch VO Import ("all conversations")

**Date:** 2026-07-04
**Status:** Approved
**Gaps.md entry:** "Project-wide batch VO import has no entry point" (resolves it; the
orphaned `Menu_BatchImportVoAll` / `ToolTip_Menu_BatchImportVoAll` strings finally get
their consumer).

## Problem

Batch VO import exists per conversation (Test menu + canvas context menu), but a mod
that voices lines across several conversations forces the user to open each conversation
and run the dialog once per file. The strings for an "all conversations" menu item have
existed in `Strings.axaml` since 2026-06-26 with nothing referencing them.

## Scope decision

"All conversations" means **the conversations the project patches** (`project.Patches`)
— the same universe `VoOrphanScanner` walks. It does *not* mean every shipped
`.conversationbundle`: a pure VO-replacement of an untouched vanilla conversation is
served by opening that conversation and using the per-conversation flow. (Rejected:
scanning all 1000+ shipped conversations — slow, needs progress/cancel UI, and produces
a mostly irrelevant row list.)

## Decision — approach

A new static service `ProjectVoRowScanner` (`DialogEditor.ViewModels/Services`),
mirroring `VoOrphanScanner` file-for-file: walk `project.Patches`, load vanilla + patch
per conversation, resolve VO paths per node, and return the same `BatchVoRowViewModel`
rows the per-conversation builder produces. The rows feed the **existing**
`BatchVoImportDialog` via `BatchVoImportViewModel(rows, importer,
isSingleConversation: false)` — the multi-conversation mode (visible Conversation
column) has been built and dormant since 2026-06-26.

Rejected alternatives:

- **Extract a shared conversation-walking core** for `VoOrphanScanner` + the new
  scanner: the consumers want different per-node outputs (expected filenames vs. row
  VMs), so the shared core degenerates into a `foreach` with a callback — abstraction
  for its own sake at ~20 duplicated lines.
- **Grow the Validate Voice-Over window** into a project-wide import surface: conflates
  validation with import and abandons the working batch dialog.

## Design

### Menu item & command

- `MainWindow.axaml`, Test menu, directly after the per-conversation "Batch import VO…"
  item (the Test menu is the established home of VO tooling).
- Header: existing `Menu_BatchImportVoAll`; `ToolTip.Tip` + `AutomationProperties.HelpText`:
  existing `ToolTip_Menu_BatchImportVoAll`. If the existing tooltip value does not name
  the enablement conditions (open project + PoE2 game folder), reword the **value only**
  (keys unchanged), matching the 2026-07-04 menu-entry design's tooltip convention.
- Command: new `BatchImportVoAllCommand` on **`MainWindowViewModel`** (it needs
  `_project`, unlike the per-conversation command on `ConversationViewModel`).
  No `IsEnabled`/`IsVisible` bindings — visible-but-disabled via `CanExecute`, same as
  the per-conversation entry.
- `CanBatchImportVoAll`: `_project`, `_provider`, and `_projectPath` are non-null and
  the active game is PoE2 (same game gate as `CanValidateVO`, minus its open-conversation
  requirement — no conversation needs to be open). `NotifyCanExecuteChanged()` raised
  from `SetProject` and game-folder load, where `CanValidateVO`'s notifications already
  live. `_projectPath` is required because row destinations live in `_vo/` next to the
  project file — an unsaved new project cannot batch-import.
- **No "has voiced nodes" pre-gate** — that would require the full scan on every
  CanExecute poll. An empty scan reports instead (below).

### Command flow

```
BatchImportVoAllAsync:
  rows = await Task.Run(() => ProjectVoRowScanner.BuildRows(...))
  if rows empty → StatusText = Loc.Get("Status_BatchImportVoAllEmpty"); return
  await ShowBatchVoImportAll(rows)          // delegate wired in MainWindow.axaml.cs
  Detail.Refresh()                          // VO status row may have flipped to ✓
```

- `Task.Run` because each conversation is a JSON parse plus per-node disk checks
  (`VoValidationViewModel` precedent). No cancellation: a mod's patch count is small
  (typically a handful of conversations); the scan is bounded and fast.
- `ShowBatchVoImportAll: Func<IReadOnlyList<BatchVoRowViewModel>, Task>` — wired in
  `MainWindow.axaml.cs` beside `Canvas.ShowBatchVoImport`, constructing
  `BatchVoImportViewModel(rows, voImporter, isSingleConversation: false)` and showing
  the same `BatchVoImportDialog`.
- New string `Status_BatchImportVoAllEmpty`: "No voiced nodes found in this project's
  conversations." (no count — no pluralisation needed).

### `ProjectVoRowScanner.BuildRows`

```csharp
public static IReadOnlyList<BatchVoRowViewModel> BuildRows(
    DialogProject project,
    IGameDataProvider provider,
    string projectPath,
    string gameRoot,
    string activeGameId,
    string? openConversationName = null,
    ConversationEditSnapshot? openSnapshot = null)
```

Per `(convName, patch)` in `project.Patches`:

- **Open conversation:** use `openSnapshot` (live canvas snapshot — unsaved edits
  count), exactly like `VoOrphanScanner`.
- **Others:** `provider.FindConversation` → `ConversationSnapshotBuilder.Build` (or an
  empty snapshot for a new conversation with no vanilla base) →
  `PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true)`. Unreadable conversation:
  `AppLog.Warn` + skip (orphan-scanner precedent — never fail the whole scan for one
  bad file).
- **Per node** (mirrors `ConversationViewModel.BuildBatchVoRows`): skip unless `HasVO`
  or `ExternalVO` is set; `VoPathResolver.Check(...)`; skip `null` /
  `VoPresence.NotApplicable` / null primary path; destination = `_vo/` +
  path-relative-to-game-VO-root, `_fem.wem` sibling for the female slot;
  `isAliased: !string.IsNullOrEmpty(node.ExternalVO)` (aliased rows keep the existing
  shown-but-excluded-from-import behaviour).
- **Female-text presence** for non-open conversations comes from the patch's
  translations overlay (`patch.Translations[provider.Language]`), same as
  `VoOrphanScanner`'s `femOverride` — added nodes carry no text in the applied snapshot
  (`[JsonIgnore]`).
- **Text preview:** snapshot `DefaultText` when non-empty; else the translation entry's
  `DefaultText` (covers added nodes); else "Node {id}". Same 60-char truncation as the
  per-conversation builder.
- **Sort:** conversation name (`OrdinalIgnoreCase`), then node id.

The per-conversation `BuildBatchVoRows` stays untouched.

## Error handling

- Per-conversation load failure inside the scan: `AppLog.Warn`, skip that conversation.
- Scan-level unexpected failure: caught in the command, `AppLog.Error` + status-bar
  error text (existing `Loc` error-string conventions).
- Import-phase errors are already handled per-row by `BatchVoImportViewModel`.

## Testing (strict TDD)

1. **`ProjectVoRowScannerTests`** (mirror the `VoOrphanScanner` test fixture: stub
   `IGameDataProvider`, temp dirs): rows span multiple patched conversations, sorted by
   conversation then node id; open conversation uses the live snapshot over the saved
   patch; unreadable conversation is skipped, others survive; nodes without VO are
   skipped; aliased nodes flagged `IsAliased`; added-node preview falls back to the
   patch translation text.
2. **`MainWindowViewModel` gating:** `CanBatchImportVoAll` false with no project /
   no game folder / no project path; true with all three; empty scan sets
   `Status_BatchImportVoAllEmpty` on `StatusText`.
3. **Declarative XAML** (menu item, tooltip) is auto-enforced by the existing
   structural suites (`AutomationHelpTextTests` etc.); dialog multi-conversation column
   behaviour is existing, already-shipped code.

## Gaps.md

Rewrite the "Project-wide batch VO import has no entry point" entry as resolved
(2026-07-04): Test-menu item wired to `ProjectVoRowScanner` + the dormant
`isSingleConversation: false` dialog mode; scope = patched conversations only.
