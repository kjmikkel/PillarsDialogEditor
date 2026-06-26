# Batch VO Import & Mod VO Design

**Date:** 2026-06-26

## Overview

Two related features that extend the existing single-node VO import workflow to handle multiple nodes at once, sharing a single dialog and ViewModel:

1. **Conversation-level batch import** — open a table of all VO nodes in the current conversation and browse for files per row
2. **Project-level Mod VO** — same table, but spanning all conversations touched by the current project

Both features pre-populate the table automatically (no canvas multi-select required). Rows where the user picks no file are silently skipped on import.

## Shared Components

### `BatchVoRowViewModel`

One instance per VO node. Lives in `DialogEditor.ViewModels`.

| Property | Type | Notes |
|---|---|---|
| `ConversationName` | `string` | Shown in the Conversation column; hidden when all rows are from the same conversation |
| `NodeId` | `int` | Zero-padded to 4 digits in display |
| `TextPreview` | `string` | First 60 chars of the node's default text (same `BuildPreview` logic as `VoValidationViewModel`) |
| `VoStatus` | `VoPresence` | Pre-computed via `VoPathResolver.Check` on dialog open — indicates Found/Missing for the game's existing file |
| `DestPrimaryPath` | `string` | Expected `_vo/`-relative destination for the primary .wem; computed once, never changes |
| `DestFemPath` | `string` | Expected destination for the female variant (always set when speaker is known, even if no female file exists yet) |
| `PrimarySourcePath` | `string?` | Set when the user browses; null = skip this row on import |
| `FemSourcePath` | `string?` | Set when the user browses; null = no female file for this row |
| `RowStatus` | `BatchRowStatus` | `Pending / Importing / Done / Error` — updated live during import run |
| `ErrorMessage` | `string?` | Populated on per-row error; shown inline |

### `BatchVoImportViewModel`

Lives in `DialogEditor.ViewModels`. Injected with `IVoImporter`.

- `ObservableCollection<BatchVoRowViewModel> Rows` — all rows, unfiltered
- `ObservableCollection<BatchVoRowViewModel> VisibleRows` — rebuilt from `Rows` whenever `ShowOnlyMissing` changes; bound to the DataGrid (Avalonia has no WPF-style `ICollectionView`)
- `bool ShowOnlyMissing` (default `true`) — when true, `VisibleRows` contains only rows where `VoStatus != VoPresence.Found`; when false, all rows are shown
- `WemQuality Quality` (default `Medium`) — global quality applied to all WAV encodes; same Low/Medium/High as `VoImportDialog`
- `bool IsImporting` — true while the import loop runs
- `string ProgressText` — "3 / 12 imported" shown during import
- `ImportCommand` — enabled when `!IsImporting && Rows.Any(r => r.PrimarySourcePath != null)`; iterates rows with a source, calls `VoImporter.ImportAsync` per row sequentially, updates `RowStatus` as it goes; continues on per-row errors; passes `CancellationToken`
- `CancelCommand` — cancels the in-progress import; no-op when not importing

Import loop error handling: catch per row, set `RowStatus = Error` + `ErrorMessage`, log via `AppLog.Error`, continue to next row. `OperationCanceledException` breaks the loop silently.

### `BatchVoImportDialog`

Lives in `DialogEditor.Avalonia/Views`. Avalonia `Window`, parameterless ctor for AVLN3001.

- **Header row:** "Show only missing" toggle (CheckBox) + quality radio group (Low / Medium / High) + row count label ("N nodes")
- **DataGrid** (scrollable, fill remaining height):
  - Column: Conversation (hidden when `IsSingleConversation = true`)
  - Column: Node ID
  - Column: Text preview (fills remaining width, `TextTrimming=CharacterEllipsis`)
  - Column: VO Status icon (✓ / ✗)
  - Column: Primary source (filename label + Browse button + Play button + Clear button)
  - Column: Female source (filename label + Browse button + Play button + Clear button)
  - Column: Row status icon (blank / spinner / ✓ / ✗ with tooltip showing `ErrorMessage`)
- **Footer:** progress bar + `ProgressText` label (hidden when not importing) + Import button + Cancel button
- Import button label: "Import" (not OK); disabled until at least one row has a source file
- Cancel always enabled (closes dialog when not importing; cancels import when importing)
- Play buttons use the shared `IVoAudioPlayer` — clicking play on any row stops whatever is currently playing and starts the new file (same ▶/■ glyph logic as `VoImportDialog`)
- All strings in `Strings.axaml` (no hardcoded user-visible text)
- Every interactive control has `ToolTip.Tip` and `AutomationProperties.HelpText`

### Two implementation plans

The shared dialog + conversation-level entry point is **Plan 1**. The project-level scan + menu item is **Plan 2**. Plan 2 depends on Plan 1.

---

## Plan 1: Shared Dialog + Conversation-Level Batch Import

### Entry point

A new **"Batch Import Voice-Over…"** context menu item on the canvas (alongside the existing per-node "Import Voice-Over…") and a matching item under the Conversation menu. Enabled when:
- The project is saved (`ProjectPath != null`)
- The current conversation has at least one VO node (i.e. `VoPathResolver.Check` returns non-`NotApplicable` for at least one node)

### Row construction

`ConversationViewModel` exposes a new method `BuildBatchVoRows(string voDir, string gameRoot, string activeGameId)`:
- Iterates `Canvas.BuildSnapshot().Nodes`
- Calls `VoPathResolver.Check` per node
- Skips `null` and `NotApplicable` results
- Returns `IReadOnlyList<BatchVoRowViewModel>` sorted by `NodeId`

`MainWindow.axaml.cs` wires the entry point:
```csharp
vm.Canvas.ShowBatchVoImport = async () =>
{
    var rows = vm.Canvas.BuildBatchVoRows(voDir, gameRoot, activeGameId);
    var batchVm = new BatchVoImportViewModel(rows, voImporter);
    var dialog = new BatchVoImportDialog(batchVm, audioPlayer, isSingleConversation: true);
    await dialog.ShowDialog(this);
    vm.Detail.NotifyAllProxies(); // refresh VO status rows
};
```

### Post-import refresh

After `BatchVoImportDialog` closes, `vm.Detail.NotifyAllProxies()` is called unconditionally (same pattern as per-node import) so VO status icons on the canvas update immediately.

---

## Plan 2: Project-Level Mod VO

### Entry point

**"File › Batch Import Voice-Over… (All Conversations)"** menu item. Enabled when:
- The project is saved
- The project has at least one patched conversation or a currently-open conversation

### Row construction

`MainWindowViewModel` builds rows from all conversations the project touches:

```csharp
IReadOnlyList<NodeEditSnapshot> FetchNodesForVo(string name)
{
    if (name == _currentFile?.Name)
        return Canvas.BuildSnapshot().Nodes;
    if (_project.Patches.TryGetValue(name, out var patch))
        return PatchApplier.Apply(new ConversationEditSnapshot([]), patch).Nodes;
    return [];
}

var allNames = new[] { _currentFile?.Name }
    .Concat(_project.Patches.Keys)
    .Where(n => n != null)
    .Distinct()
    .OrderBy(n => n)
    .ToList();
```

For each conversation name, `FetchNodesForVo` returns the relevant nodes; `VoPathResolver.Check` filters to VO nodes; `BatchVoRowViewModel` is constructed with `ConversationName` set. The `isSingleConversation: false` flag is passed to the dialog so the Conversation column is visible.

The scan runs synchronously (it touches only already-loaded data — no disk reads for the node list itself). Dest paths use the project's `_vo/` directory.

### Post-import refresh

After the dialog closes, `vm.Detail.NotifyAllProxies()` refreshes the current conversation's canvas. Other conversations reflect changes next time they are opened.

---

## Strings Required

All new keys go into `Strings.axaml`:

```
BatchVoImport_Title_Single        "Batch Import Voice-Over"
BatchVoImport_Title_All           "Batch Import Voice-Over — All Conversations"
BatchVoImport_ShowOnlyMissing     "Show only missing"
BatchVoImport_ImportButton        "Import"
BatchVoImport_NodeColumn          "Node"
BatchVoImport_ConversationColumn  "Conversation"
BatchVoImport_TextColumn          "Text"
BatchVoImport_StatusColumn        "Status"
BatchVoImport_PrimaryColumn       "Primary"
BatchVoImport_FemColumn           "Female"
BatchVoImport_RowStatusColumn     "Result"
BatchVoImport_RowCount            "{0} nodes"
BatchVoImport_Progress            "{0} / {1} imported"
BatchVoImport_QualityLabel        "Quality (WAV only)"
ToolTip_BatchVoImport             "Open a table of all VO nodes in this conversation and browse for source files to import."
ToolTip_BatchVoImportAll          "Open a table of all VO nodes across every conversation in this project and browse for source files to import."
ToolTip_BatchBrowsePrimary        "Browse for a .wem or .wav file for this node's primary voice-over slot."
ToolTip_BatchBrowseFem            "Browse for a .wem or .wav file for this node's female voice-over slot."
ToolTip_BatchClearPrimary         "Clear the selected primary voice-over file for this row."
ToolTip_BatchClearFem             "Clear the selected female voice-over file for this row."
ToolTip_BatchPlayPrimary          "Play the selected primary voice-over file for this row."
ToolTip_BatchPlayFem              "Play the selected female voice-over file for this row."
ToolTip_BatchShowOnlyMissing      "When checked, hides rows whose VO file already exists in the game folder."
Menu_BatchImportVo                "Batch Import Voice-Over…"
Menu_BatchImportVoAll             "Batch Import Voice-Over… (All Conversations)"
ToolTip_Menu_BatchImportVo        "Import voice-over files for multiple nodes in the current conversation at once."
ToolTip_Menu_BatchImportVoAll     "Import voice-over files for multiple nodes across all conversations in this project at once."
```

## Error Handling

- Per-row errors: catch, log via `AppLog.Error`, set `RowStatus = Error` + `ErrorMessage`, continue
- `OperationCanceledException`: break loop silently, leave remaining rows as `Pending`
- No bare `catch {}` blocks

## Testing

- `BatchVoImportViewModelTests`: `ImportCommand_SkipsRowsWithNoSourcePath`, `ImportCommand_SetsRowStatusDone_OnSuccess`, `ImportCommand_SetsRowStatusError_OnFailure`, `ImportCommand_StopsOnCancellation`, `ShowOnlyMissing_FiltersFoundRows`
- `ConversationViewModelTests`: `BuildBatchVoRows_ExcludesNotApplicableNodes`, `BuildBatchVoRows_SortsByNodeId`
- No headless UI tests for the dialog (consistent with existing dialog precedent)
