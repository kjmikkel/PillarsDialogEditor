# Yarn Spinner Import — Skipped-Construct Warnings Design

## Context

The Yarn Spinner importer (`DialogEditor.Core/Import/YarnSpinnerImporter.cs`) handles
basic dialogue lines and player choices but silently drops every line beginning with
`<<` — conditional blocks (`<<if>>`, `<<elseif>>`, `<<else>>`, `<<endif>>`), variable
assignments (`<<set>>`), and commands (`<<command>>`). A writer importing a `.yarn`
file that uses these constructs loses that content with no indication anything was
missing.

This feature surfaces a warning, in **both** a modal dialog and the status bar, listing
which Yarn constructs were skipped and how many times each occurred. The importer still
drops the constructs — this is a transparency feature, not a parsing feature.

## Scope

- **In scope:** detecting and tallying skipped `<<...>>` constructs in the Yarn importer;
  carrying that information out of `Import()`; showing it in a dialog and the status bar.
- **Out of scope:** actually importing conditions/commands into the conversation model;
  any change to CSV or JSON importers beyond returning an empty warning list.

## Architecture

Warnings travel with the import result. `ImportedConversation` gains a `Warnings` field
(empty for importers that never skip anything). `MainWindowViewModel.ImportConversation`
inspects the field after parsing and, if non-empty, awaits a UI callback to show a modal
dialog before continuing the existing import flow. The final status message gains a
skipped-constructs suffix when warnings were present.

## Data Layer — `DialogEditor.Core/Import/`

New record and an added field on the existing record (`IDialogImporter.cs`):

```csharp
public record ImportWarning(string Construct, int Count);

public record ImportedConversation(
    string SuggestedName,
    IReadOnlyList<NodeEditSnapshot> Nodes,
    IReadOnlyList<NodeTranslation>  Texts,
    IReadOnlyList<ImportWarning>    Warnings   // empty for CSV/JSON
);
```

`Construct` is the raw Yarn keyword as written in the file (the first whitespace- or
delimiter-bounded token after `<<`), e.g. `if`, `set`, `endif`. `Count` is the number of
occurrences of that keyword across the whole file.

`YarnSpinnerImporter` already skips `<<...>>` lines in `GeneratePendingNodes`. The
importer will additionally:

1. As it encounters each `<<...>>` line, extract the keyword — the run of characters
   after the leading `<<`, stopping at the first whitespace or the closing `>>`.
2. Tally occurrences per distinct keyword in a `Dictionary<string, int>` keyed
   ordinally.
3. Build the `Warnings` list (one `ImportWarning` per distinct keyword) and pass it to
   the `ImportedConversation` constructor.

Example: a file containing `<<if $x>>`, `<<if $y>>`, and `<<endif>>` yields
`[("if", 2), ("endif", 1)]`.

The three non-Yarn importers — `CsvDialogImporter`, `JsonDialogImporter`, and
`ArticyXmlImporter` — pass an empty list (`[]`) for `Warnings`. Their call sites are
updated only to satisfy the new constructor parameter; their behaviour is otherwise
unchanged. (All four `new ImportedConversation(...)` sites must be updated, or the code
will not compile.)

## ViewModel Layer — `DialogEditor.ViewModels/`

New callback on `MainWindowViewModel`, mirroring the `RequestConversationName` pattern:

```csharp
/// Set by the UI layer to show an informational dialog listing import warnings.
public Func<IReadOnlyList<ImportWarning>, Task>? ShowImportWarnings { get; set; }
```

In `ImportConversation`, after a successful `Import()` and before requesting the
conversation name, if warnings exist the callback is awaited:

```csharp
imported = importer.Import(path);

if (imported.Warnings.Count > 0)
    await (ShowImportWarnings?.Invoke(imported.Warnings) ?? Task.CompletedTask);
```

The existing import flow (name prompt, duplicate check, patch build, auto-layout, load)
is unchanged.

The final status text gains a skipped-constructs variant. Two resource keys:

- `Status_ImportConversationAdded` — existing clean-import message
  (`Imported '{0}' ({1} nodes).`), unchanged.
- `Status_ImportConversationAddedWithWarnings` — new, adds the suffix
  (`Imported '{0}' ({1} nodes). Skipped Yarn constructs: {2}.`), where `{2}` is a
  comma-joined list of `<<keyword>>` tokens.

When `imported.Warnings` is non-empty, the with-warnings key is used; otherwise the clean
key. CSV/JSON imports always take the clean path.

## UI Layer — `DialogEditor.Avalonia/`

New `ImportWarningsDialog` window (`Views/ImportWarningsDialog.axaml` + code-behind):

- Modal, `WindowStartupLocation="CenterOwner"`, `CanResize="False"`.
- `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"` (every window carries the app
  icon).
- Body: an explanatory sentence, then a scrollable `ItemsControl` listing each warning as
  `• <<keyword>> — N occurrence(s)`, then a footer line telling the writer to check the
  original file for the dropped content.
- A single **OK** button that closes the dialog. No cancel — purely informational.
- Constructor takes `IReadOnlyList<ImportWarning>` and exposes it for binding. A
  parameterless constructor is also provided so the Avalonia XAML compiler can complete
  its first-pass type analysis (avoids the AVLN3000 clean-build failure seen with other
  windows in this project).

`MainWindow.axaml.cs` wires the callback in the constructor alongside the other
`Request*`/`Show*` assignments:

```csharp
vm.ShowImportWarnings = async warnings =>
{
    var dialog = new ImportWarningsDialog(warnings);
    await dialog.ShowDialog(this);
};
```

New string keys in `DialogEditor.Avalonia/Resources/Strings.axaml`:

- `ImportWarnings_Title` — dialog title.
- `ImportWarnings_Body` — explanatory sentence.
- `ImportWarnings_Footer` — "check the original file" note.
- `ImportWarnings_OccurrenceSuffix` — the "occurrence(s)" label.
- `ImportWarnings_Ok` — OK button label.
- `Status_ImportConversationAddedWithWarnings` — the with-warnings status format string.

All existing keys are unchanged.

## Error Handling

No new exception paths. The importer's existing `FormatException` on empty/invalid files
is unchanged. The warning tally is pure in-memory counting that cannot fail. If the
`ShowImportWarnings` callback is unset (e.g. in headless tests), the null-conditional
invocation falls back to `Task.CompletedTask` and the import proceeds — consistent with
how the other `Request*` callbacks degrade.

## Testing

**Core (`DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs`):**
- A `.yarn` file with `<<if>>`/`<<endif>>`/`<<set>>` produces the expected
  `Warnings` list with correct keywords and counts.
- A `.yarn` file with no `<<...>>` lines produces an empty `Warnings` list.
- Repeated keywords are tallied (two `<<if>>` → `Count == 2`).
- The skipped constructs still do not appear as nodes (existing behaviour preserved).

**Core (`CsvDialogImporterTests`, `JsonDialogImporterTests`, `ArticyXmlImporterTests`):**
- Each importer returns an empty `Warnings` list (one assertion each, added to existing
  tests).

**ViewModel (`DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`):**
- Importing a Yarn file with warnings invokes the `ShowImportWarnings` callback with the
  warning list.
- Importing a file with no warnings does not invoke the callback.
- The status text uses the with-warnings format when warnings are present and the clean
  format otherwise.

**UI (`DialogEditor.Tests/Views/ImportWarningsDialogTests.cs`):**
- An `[AvaloniaFact]` headless test verifying the dialog lists one row per warning.
- An `[AvaloniaFact]` verifying the OK button closes the dialog.

## Verification

1. `dotnet test DialogEditor.Tests` — all tests pass.
2. `dotnet build DialogEditor.Avalonia` — 0 errors.
3. Manual: import a `.yarn` file containing `<<if>>`/`<<set>>` — dialog appears listing
   the skipped constructs, status bar shows the suffix after dismissal.
4. Manual: import a `.yarn` file with only plain dialogue — no dialog, clean status
   message.
5. Manual: import a `.csv` and a `.json` — no dialog, clean status message.
