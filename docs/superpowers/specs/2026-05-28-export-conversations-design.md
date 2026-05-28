# Export Conversations — Design Spec

## Goal

Add a one-shot export path from the editor to CSV, JSON (purpose-built schema), and Yarn Spinner `.yarn` format. Writers who authored conversations in the editor can hand them back to external tools. Articy Draft XML export is explicitly not supported (documented in the UI).

## UI Differentiation from Translation Export

The existing "Export for Translation…" exports a flat text table (DefaultText / FemaleText) for human translators. The new "Export Conversation…" exports full conversation structure (nodes, links, speaker categories, display types). These must not be conflated:

- **Menu placement** — "Export Conversation…" sits immediately below "Import Conversation…", separated from the translation pair by the existing separator:

  ```
  Import Conversation…
  Export Conversation…          ← new
  ────────────────────────────
  Export for Translation…
  Import Translation…
  ```

- **Tooltip** — explicitly states this exports structure, not text for translators.

## Architecture

### Core export layer — `DialogEditor.Core/Export/`

Mirrors `DialogEditor.Core/Import/` exactly.

```csharp
public record ConversationExport(
    string Name,
    IReadOnlyList<NodeEditSnapshot> Nodes  // links live on each node
);

public interface IDialogExporter
{
    string FileExtension { get; }   // e.g. ".csv", includes leading dot
    void Export(ConversationExport conversation, string path);
}
```

Files:
| File | Responsibility |
|------|----------------|
| `IDialogExporter.cs` | Interface + `ConversationExport` record |
| `CsvDialogExporter.cs` | Writes same columns the importer reads |
| `JsonDialogExporter.cs` | Writes same schema the importer reads |
| `YarnSpinnerExporter.cs` | Writes `title:` blocks with speaker lines and `->` choices |
| `DialogExporterFactory.cs` | Format string → exporter lookup; `AllFileTypes` list |

**CSV** — header row `NodeId,SpeakerCategory,DefaultText,FemaleText,LinksTo,DisplayType,Persistence`. `LinksTo` is semicolon-separated target IDs. Fields containing commas or newlines are quoted. Round-trip lossless for these fields.

**JSON** — same schema as `JsonDialogImporter` expects:
```json
{ "name": "conv_name", "nodes": [ { "id": 1, "speakerCategory": "Npc", ... } ] }
```

**Yarn Spinner** — one `title:` block per node (named by NodeId). Speaker lines as `SpeakerCategory: DefaultText`. Player-choice nodes as `-> DefaultText [[TargetNodeId]]`. Conditions and scripts have no Yarn equivalent and are silently omitted (no data in `NodeEditSnapshot` maps to Yarn conditions).

### ViewModel — `ExportConversationsViewModel`

```csharp
public class ExportConversationsViewModel(
    IReadOnlyList<string> conversationNames,
    string? currentConversationName,
    Func<string, IReadOnlyList<NodeEditSnapshot>> nodesFetch,
    IFilePicker filePicker,
    IFolderPicker folderPicker)
```

- `ObservableCollection<ConversationExportItem>` — each item has `Name` (string) and `IsChecked` (bool). The item matching `currentConversationName` is pre-checked; all others start unchecked.
- `SelectAllCommand` / `SelectNoneCommand` — set all `IsChecked` accordingly.
- `SelectedFormat` — string, default `"csv"`.
- `ExportCommand` — `CanExecute` = at least one item is checked.
  - One checked → `filePicker.PickSaveFileAsync(...)` → `exporter.Export(conv, path)`
  - Multiple checked → `folderPicker.PickFolderAsync(...)` → one file per conversation in that folder, named `{name}.{ext}`
- `StatusText` — string property, shown in the window after export completes or on error.

### MainWindowViewModel integration

```csharp
public Func<ExportConversationsViewModel, Task>? ShowExportConversations { get; set; }

[RelayCommand(CanExecute = nameof(IsProjectLoaded))]
private async Task ExportConversations()
{
    if (_project is null) return;
    var evm = new ExportConversationsViewModel(
        _project.Patches.Keys.ToList(),
        CurrentConversationName,
        name => _project.Patches[name].Nodes,
        _filePicker,
        _folderPicker);
    if (ShowExportConversations is not null)
        await ShowExportConversations(evm);
}
```

`MainWindow` wires the callback:
```csharp
vm.ShowExportConversations = async evm =>
{
    var window = new ExportConversationsWindow(evm);
    await window.ShowDialog(this);
};
```

`ExportConversationsCommand.NotifyCanExecuteChanged()` is called alongside existing commands in `OnProjectChanged`.

### View — `ExportConversationsWindow`

Modal window (`ShowDialog`). Three zones:

1. **Conversation list** — `ScrollViewer` containing `ItemsControl` bound to `ConversationItems`. Each row: `CheckBox` bound to `IsChecked`, `TextBlock` with `Name`. Two link-style buttons above: "Select All" / "Select None".

2. **Format selector** — four `RadioButton`s:
   - CSV (default)
   - JSON
   - Yarn Spinner
   - Articy Draft XML — `IsEnabled="False"`, `ToolTip.Tip`: *"Articy Draft XML export is not supported. Articy's format requires proprietary internal IDs and template definitions that the editor does not store."*

3. **Footer** — `TextBlock` for `StatusText` (left-aligned, muted colour), Export button (right-aligned, bound to `ExportCommand`, label "Export…"), Close button.

### Strings

New keys in `Strings.axaml`:
```
Menu_ExportConversation           Export Conversation…
ToolTip_ExportConversation        Export selected conversations to CSV, JSON, or Yarn Spinner.
                                  This exports conversation structure (nodes, links, speaker
                                  categories), not translated text.
ExportConversations_Title         Export Conversations
ExportConversations_SelectAll     Select All
ExportConversations_SelectNone    Select None
ExportConversations_Format        Format
ExportConversations_ArticyNote    Articy Draft XML export is not supported. Articy's format
                                  requires proprietary internal IDs and template definitions
                                  that the editor does not store.
ExportConversations_Export        Export…
ExportConversations_Close         Close
Status_ExportConversationsSaved   Exported {0} conversation(s) to {1}.
Status_ExportConversationsError   Export failed: {1}
```

## Testing

**Unit tests — no Avalonia required:**

- `CsvDialogExporterTests` — export a 3-node conversation with links; read back with `CsvDialogImporter`; assert node count, speaker categories, link targets round-trip correctly. Also test that fields containing commas are quoted.
- `JsonDialogExporterTests` — export and re-import via `JsonDialogImporter`; assert round-trip.
- `YarnSpinnerExporterTests` — export a conversation with an NPC line, a player choice, and a target node; assert the `.yarn` text contains the expected `title:`, speaker lines, and `->` entries.
- `ExportConversationsViewModelTests` — SelectAll checks all items; SelectNone clears all; current conversation is pre-checked; `ExportCommand.CanExecute` is false when nothing checked, true when one is checked.

**Headless Avalonia tests:**

- `ExportConversationsWindowTests` — Articy `RadioButton` is `IsEnabled == false`; Export button is disabled when nothing checked; Export button enables after checking one item.

## Files to Create / Modify

| File | Action |
|------|--------|
| `DialogEditor.Core/Export/IDialogExporter.cs` | Create |
| `DialogEditor.Core/Export/CsvDialogExporter.cs` | Create |
| `DialogEditor.Core/Export/JsonDialogExporter.cs` | Create |
| `DialogEditor.Core/Export/YarnSpinnerExporter.cs` | Create |
| `DialogEditor.Core/Export/DialogExporterFactory.cs` | Create |
| `DialogEditor.ViewModels/ViewModels/ExportConversationsViewModel.cs` | Create |
| `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Add command + callback |
| `DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml` | Create |
| `DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml.cs` | Create |
| `DialogEditor.Avalonia/Views/MainWindow.axaml` | Add menu item |
| `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Wire `ShowExportConversations` callback |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | New keys |
| `DialogEditor.Tests/Export/CsvDialogExporterTests.cs` | Create |
| `DialogEditor.Tests/Export/JsonDialogExporterTests.cs` | Create |
| `DialogEditor.Tests/Export/YarnSpinnerExporterTests.cs` | Create |
| `DialogEditor.Tests/ViewModels/ExportConversationsViewModelTests.cs` | Create |
| `DialogEditor.Tests/Views/ExportConversationsWindowTests.cs` | Create |
