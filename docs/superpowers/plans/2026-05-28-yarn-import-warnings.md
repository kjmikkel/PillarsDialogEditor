# Yarn Import Skipped-Construct Warnings Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Surface the Yarn constructs (`<<if>>`, `<<set>>`, `<<command>>`, …) that the Yarn Spinner importer silently drops, in both a modal dialog and the status bar.

**Architecture:** Warnings travel on the `ImportedConversation` record. `YarnSpinnerImporter` tallies each skipped `<<keyword>>`. `MainWindowViewModel.ImportConversation` inspects the field after parsing and, if non-empty, awaits a `ShowImportWarnings` UI callback that opens a new informational `ImportWarningsDialog`, then sets a status message with a skipped-constructs suffix.

**Tech Stack:** C# 12 / .NET 8, Avalonia 11.3.14, xUnit, CommunityToolkit.Mvvm, `Avalonia.Headless.XUnit` for headless dialog tests.

---

## Background — Conventions This Plan Follows

- **TDD is mandatory** (see `CLAUDE.md`): write the failing test first, watch it fail, then implement.
- **No hard-coded user-visible strings**: every label/status string lives in `DialogEditor.Avalonia/Resources/Strings.axaml` and is referenced via `{StaticResource ...}` (XAML) or `Loc.Get`/`Loc.Format` (C#).
- **Every `<Window>` carries the app icon**: `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- **Every interactive control needs a `ToolTip.Tip`** unless it is 100% self-explanatory (OK/Cancel on a confirmation dialog are the documented exception — the dialog's single OK button qualifies).
- **Avalonia clean-build quirk:** any new `<Window>` with an `x:Class` needs a public **parameterless constructor**, or the XAML compiler aborts with `AVLN3000` and leaves `App.axaml` precompiled XAML unembedded (this breaks *all* headless tests). Always add `public ImportWarningsDialog() => InitializeComponent();` alongside the real constructor.
- **Test command (run from repo root):**
  `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~<TestClass>"`
- **Full suite:** `dotnet test DialogEditor.Tests`

## File Structure

| File | Responsibility |
|------|----------------|
| `DialogEditor.Core/Import/IDialogImporter.cs` | Modify — add `ImportWarning` record + `Warnings` field on `ImportedConversation` |
| `DialogEditor.Core/Import/CsvDialogImporter.cs` | Modify — pass `[]` for `Warnings` |
| `DialogEditor.Core/Import/JsonDialogImporter.cs` | Modify — pass `[]` for `Warnings` |
| `DialogEditor.Core/Import/ArticyXmlImporter.cs` | Modify — pass `[]` for `Warnings` |
| `DialogEditor.Core/Import/YarnSpinnerImporter.cs` | Modify — tally skipped `<<...>>` keywords, return them |
| `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Modify — `ShowImportWarnings` callback + status suffix |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | Modify — dialog + status string keys |
| `DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml` | Create — informational dialog |
| `DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml.cs` | Create — code-behind |
| `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Modify — wire `ShowImportWarnings` |
| `DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs` | Modify — warning tally tests |
| `DialogEditor.Tests/Import/CsvDialogImporterTests.cs` | Modify — empty-warnings assertion |
| `DialogEditor.Tests/Import/JsonDialogImporterTests.cs` | Modify — empty-warnings assertion |
| `DialogEditor.Tests/Import/ArticyXmlImporterTests.cs` | Modify — empty-warnings assertion |
| `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs` | Modify — callback + status tests |
| `DialogEditor.Tests/Views/ImportWarningsDialogTests.cs` | Create — headless dialog tests |
| `Gaps.md` | Modify — remove the Yarn-import gap entry |

---

### Task 1: Add `ImportWarning` to the import contract

Add the warning type and field, and update the three non-Yarn importers + Yarn to compile by passing an empty list. This task makes the contract change and keeps everything green; Yarn's real tally comes in Task 2.

**Files:**
- Modify: `DialogEditor.Core/Import/IDialogImporter.cs`
- Modify: `DialogEditor.Core/Import/CsvDialogImporter.cs:80`
- Modify: `DialogEditor.Core/Import/JsonDialogImporter.cs:67`
- Modify: `DialogEditor.Core/Import/ArticyXmlImporter.cs:77`
- Modify: `DialogEditor.Core/Import/YarnSpinnerImporter.cs:64`
- Test: `DialogEditor.Tests/Import/CsvDialogImporterTests.cs`, `JsonDialogImporterTests.cs`, `ArticyXmlImporterTests.cs`

- [ ] **Step 1: Write failing assertions in the three non-Yarn importer test classes**

In `DialogEditor.Tests/Import/CsvDialogImporterTests.cs`, add this test (adjust the helper call to match the file's existing temp-CSV helper — the class already has one used by other tests; reuse it):

```csharp
[Fact]
public void Import_Csv_HasNoWarnings()
{
    var path = WriteTempCsv("""
        NodeId,SpeakerCategory,DefaultText
        1,Npc,Hello
        """);

    var result = Importer.Import(path);

    Assert.Empty(result.Warnings);
}
```

In `DialogEditor.Tests/Import/JsonDialogImporterTests.cs`, add:

```csharp
[Fact]
public void Import_Json_HasNoWarnings()
{
    var path = WriteTempJson("""
        { "name": "c", "nodes": [ { "id": 1, "speakerCategory": "Npc", "defaultText": "Hi" } ] }
        """);

    var result = Importer.Import(path);

    Assert.Empty(result.Warnings);
}
```

In `DialogEditor.Tests/Import/ArticyXmlImporterTests.cs`, add a test that reuses the `WriteTempXml` helper and the `TwoFragmentXml` fixture constant already defined in that class (used by several existing tests):

```csharp
[Fact]
public void Import_Articy_HasNoWarnings()
{
    var path = WriteTempXml(TwoFragmentXml);
    var result = Importer.Import(path);
    Assert.Empty(result.Warnings);
}
```

- [ ] **Step 2: Run the tests — verify they fail to compile**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ImporterTests"`
Expected: **compile error** — `'ImportedConversation' does not contain a definition for 'Warnings'`.

- [ ] **Step 3: Add the `ImportWarning` record and `Warnings` field**

Replace the body of `DialogEditor.Core/Import/IDialogImporter.cs` with:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Import;

/// One distinct construct that an importer skipped, with how many times it occurred.
public record ImportWarning(string Construct, int Count);

public record ImportedConversation(
    // Hint for the UI — may be overridden by the user before the conversation is added.
    string SuggestedName,
    IReadOnlyList<NodeEditSnapshot> Nodes,
    IReadOnlyList<NodeTranslation>  Texts,
    // Constructs the importer could not represent and silently dropped. Empty when none.
    IReadOnlyList<ImportWarning>    Warnings
);

public interface IDialogImporter
{
    /// File extensions this importer handles, e.g. [".csv"]
    // Extensions include the leading dot, e.g. ".csv".
    string[] FileExtensions { get; }

    /// Parse the file at <paramref name="path"/> into an ImportedConversation.
    // Throws FormatException if the file content is not valid for this importer.
    ImportedConversation Import(string path);
}
```

- [ ] **Step 4: Update the four importer return statements to pass `[]`**

`CsvDialogImporter.cs` line ~80:

```csharp
        return new ImportedConversation(name, nodes, texts, []);
```

`JsonDialogImporter.cs` line ~67:

```csharp
        return new ImportedConversation(suggestedName, nodes, texts, []);
```

`ArticyXmlImporter.cs` line ~77:

```csharp
        return new ImportedConversation(suggestedName, nodes, texts, []);
```

`YarnSpinnerImporter.cs` line ~64 (temporary — replaced in Task 2):

```csharp
        return new ImportedConversation(name, nodes, texts, []);
```

- [ ] **Step 5: Run the tests — verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ImporterTests"`
Expected: PASS (all existing importer tests + the 3 new empty-warnings tests).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Core/Import/ DialogEditor.Tests/Import/
git commit -m "feat: add ImportWarning to import contract; importers return empty list"
```

---

### Task 2: Yarn importer tallies skipped constructs

**Files:**
- Modify: `DialogEditor.Core/Import/YarnSpinnerImporter.cs`
- Test: `DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs` (uses the existing `WriteTempYarn` helper and `Importer` field already in that class):

```csharp
// ── Skipped-construct warnings ────────────────────────────────────────

[Fact]
public void Import_SkippedConstructs_ReportedAsWarnings()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        <<if $gold > 10>>
        Merchant: You can afford this.
        <<set $seen = true>>
        <<endif>>
        ===
        """);

    var result = Importer.Import(path);

    Assert.Contains(result.Warnings, w => w.Construct == "if"  && w.Count == 1);
    Assert.Contains(result.Warnings, w => w.Construct == "set" && w.Count == 1);
    Assert.Contains(result.Warnings, w => w.Construct == "endif" && w.Count == 1);
}

[Fact]
public void Import_RepeatedConstruct_TalliesCount()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        <<if $a>>
        Npc: One.
        <<if $b>>
        Npc: Two.
        ===
        """);

    var result = Importer.Import(path);

    Assert.Contains(result.Warnings, w => w.Construct == "if" && w.Count == 2);
}

[Fact]
public void Import_NoConstructs_HasNoWarnings()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        Npc: Plain dialogue only.
        ===
        """);

    var result = Importer.Import(path);

    Assert.Empty(result.Warnings);
}
```

- [ ] **Step 2: Run the tests — verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~YarnSpinnerImporterTests"`
Expected: FAIL — the three new tests fail (warnings list is empty); existing tests still pass.

- [ ] **Step 3: Implement the tally**

In `DialogEditor.Core/Import/YarnSpinnerImporter.cs`, the `Import` method currently iterates `rawBlocks` to build nodes. Add a keyword tally that scans every block's body lines. Replace the `Import` method's tail (from the `// Second pass` comment through the `return`) so it accumulates warnings and passes them to the constructor:

First, add this helper method to the class (place it after `ParseBlocks`):

```csharp
// ── Skipped-construct tallying ────────────────────────────────────────

// Counts each distinct <<keyword>> across all block bodies. The keyword is the
// run of characters after "<<", stopping at the first whitespace or ">>".
private static List<ImportWarning> TallySkippedConstructs(IReadOnlyList<RawBlock> blocks)
{
    var counts = new Dictionary<string, int>(StringComparer.Ordinal);

    foreach (var block in blocks)
    {
        foreach (var raw in block.BodyLines)
        {
            if (!raw.StartsWith("<<", StringComparison.Ordinal))
                continue;

            var keyword = ExtractKeyword(raw);
            if (keyword.Length == 0)
                continue;

            counts[keyword] = counts.GetValueOrDefault(keyword) + 1;
        }
    }

    return counts.Select(kv => new ImportWarning(kv.Key, kv.Value)).ToList();
}

// "<<if $gold > 10>>" -> "if";  "<<endif>>" -> "endif";  "<<>>" -> "".
private static string ExtractKeyword(string line)
{
    int start = 2; // skip "<<"
    int i = start;
    while (i < line.Length
           && !char.IsWhiteSpace(line[i])
           && line[i] != '>')
    {
        i++;
    }
    return line[start..i];
}
```

Then, in `Import`, compute the warnings before building the result and pass them in. Change the final lines of `Import` from:

```csharp
        var name = Path.GetFileNameWithoutExtension(path);
        return new ImportedConversation(name, nodes, texts, []);
```

to:

```csharp
        var warnings = TallySkippedConstructs(rawBlocks);
        var name = Path.GetFileNameWithoutExtension(path);
        return new ImportedConversation(name, nodes, texts, warnings);
```

- [ ] **Step 4: Run the tests — verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~YarnSpinnerImporterTests"`
Expected: PASS (all existing + 3 new).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/Import/YarnSpinnerImporter.cs DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs
git commit -m "feat: YarnSpinnerImporter tallies skipped <<...>> constructs as warnings"
```

---

### Task 3: ViewModel callback + status suffix

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`

- [ ] **Step 1: Add the new status string key**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, immediately after the existing `Status_ImportConversationAdded` line (line ~619), add:

```xml
    <!-- {0} = conversation name, {1} = node count, {2} = comma-joined <<keyword>> list -->
    <sys:String x:Key="Status_ImportConversationAddedWithWarnings">Imported '{0}' ({1} nodes). Skipped Yarn constructs: {2}.</sys:String>
```

- [ ] **Step 2: Write the failing ViewModel tests**

Add to `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`. These tests need a provider and project injected. Add a reflection helper next to the existing `InjectProject` helper:

```csharp
/// <summary>Injects a provider into the VM's private _provider field via reflection.</summary>
private static void InjectProvider(MainWindowViewModel vm, IGameDataProvider provider)
{
    var fi = typeof(MainWindowViewModel)
        .GetField("_provider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    fi.SetValue(vm, provider);
}
```

Add these `using` directives at the top of the test file if not already present:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Import;
```

Make the test class implement `IDisposable` so the temp `.yarn` files are cleaned up. Change the class declaration from:

```csharp
public class MainWindowViewModelTests
{
    public MainWindowViewModelTests() => Loc.Configure(new StubStringProvider());
```

to:

```csharp
public class MainWindowViewModelTests : IDisposable
{
    private readonly List<string> _importTempFiles = [];

    public MainWindowViewModelTests() => Loc.Configure(new StubStringProvider());

    public void Dispose()
    {
        foreach (var f in _importTempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort cleanup */ }
    }
```

Add the temp-yarn helper and the tests:

```csharp
// ── Import warnings ───────────────────────────────────────────────────

private string WriteTempYarn(string content)
{
    var path = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(Path.GetRandomFileName(), ".yarn"));
    File.WriteAllText(path, content);
    _importTempFiles.Add(path);
    return path;
}

private static (MainWindowViewModel Vm, StubProvider Provider) MakeImportableVm(string yarnPath)
{
    var file     = new ConversationFile("stub_conv", "", "", "");
    var provider = new StubProvider(file, new ConversationEditSnapshot([]));
    var vm = new MainWindowViewModel(
        new StubDispatcher(),
        new StubFolderPicker(),
        new StubFilePicker(openResult: yarnPath));
    InjectProvider(vm, provider);
    InjectProject(vm, DialogProject.Empty("TestProject"));
    return (vm, provider);
}

[Fact]
public async Task ImportConversation_YarnWithSkippedConstructs_InvokesWarningCallback()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        <<if $x>>
        Npc: Hello.
        ===
        """);
    var (vm, _) = MakeImportableVm(path);

    IReadOnlyList<ImportWarning>? captured = null;
    vm.ShowImportWarnings = w => { captured = w; return Task.CompletedTask; };

    await vm.ImportConversationCommand.ExecuteAsync(null);

    Assert.NotNull(captured);
    Assert.Contains(captured!, w => w.Construct == "if");
}

[Fact]
public async Task ImportConversation_YarnWithoutConstructs_DoesNotInvokeCallback()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        Npc: Just dialogue.
        ===
        """);
    var (vm, _) = MakeImportableVm(path);

    var invoked = false;
    vm.ShowImportWarnings = w => { invoked = true; return Task.CompletedTask; };

    await vm.ImportConversationCommand.ExecuteAsync(null);

    Assert.False(invoked);
}

[Fact]
public async Task ImportConversation_WithWarnings_UsesWarningStatusFormat()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        <<set $y = 1>>
        Npc: Hi.
        ===
        """);
    var (vm, _) = MakeImportableVm(path);
    vm.ShowImportWarnings = _ => Task.CompletedTask;

    await vm.ImportConversationCommand.ExecuteAsync(null);

    // StubStringProvider returns the key verbatim, so StatusText equals the chosen key.
    Assert.Equal("Status_ImportConversationAddedWithWarnings", vm.StatusText);
}

[Fact]
public async Task ImportConversation_NoWarnings_UsesCleanStatusFormat()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        Npc: Hi.
        ===
        """);
    var (vm, _) = MakeImportableVm(path);

    await vm.ImportConversationCommand.ExecuteAsync(null);

    Assert.Equal("Status_ImportConversationAdded", vm.StatusText);
}
```

- [ ] **Step 3: Run the tests — verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelTests"`
Expected: FAIL — `'MainWindowViewModel' does not contain a definition for 'ShowImportWarnings'` (compile error).

- [ ] **Step 4: Add the callback property**

In `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`, add the `Export` import if missing and a new callback property after the existing `RequestLanguageCode` declaration (line ~60):

Ensure this `using` is present near the top (it was added when Export Conversations shipped — verify it exists):

```csharp
using DialogEditor.Core.Import;
```

Add the property:

```csharp
    /// Set by the UI layer to show an informational dialog listing import warnings.
    /// Awaited before the imported conversation is added to the project.
    public Func<IReadOnlyList<ImportWarning>, Task>? ShowImportWarnings { get; set; }
```

- [ ] **Step 5: Show warnings and pick the status format in `ImportConversation`**

In the same file, locate the `ImportConversation` method. Just after the successful parse (the `try { imported = importer.Import(path); }` block, around line 398), and before the name prompt (`var suggested = imported.SuggestedName;`), insert:

```csharp
        if (imported.Warnings.Count > 0)
            await (ShowImportWarnings?.Invoke(imported.Warnings) ?? Task.CompletedTask);
```

Then locate the final status line of the method (around line 449):

```csharp
        AppLog.Info($"Imported conversation '{name}' from '{path}' ({imported.Nodes.Count} nodes)");
        StatusText = Loc.Format("Status_ImportConversationAdded", name, imported.Nodes.Count);
```

Replace the `StatusText = ...` line with a branch on whether warnings were present:

```csharp
        AppLog.Info($"Imported conversation '{name}' from '{path}' ({imported.Nodes.Count} nodes)");
        if (imported.Warnings.Count > 0)
        {
            var constructs = string.Join(", ", imported.Warnings.Select(w => $"<<{w.Construct}>>"));
            StatusText = Loc.Format("Status_ImportConversationAddedWithWarnings",
                name, imported.Nodes.Count, constructs);
        }
        else
        {
            StatusText = Loc.Format("Status_ImportConversationAdded", name, imported.Nodes.Count);
        }
```

- [ ] **Step 6: Run the tests — verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelTests"`
Expected: PASS (existing + 4 new).

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat: MainWindowViewModel surfaces import warnings via callback and status suffix"
```

---

### Task 4: ImportWarningsDialog (informational modal)

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Create: `DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml`
- Create: `DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml.cs`
- Test: `DialogEditor.Tests/Views/ImportWarningsDialogTests.cs`

- [ ] **Step 1: Add the dialog string keys**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, after the `Status_ImportConversationAddedWithWarnings` line added in Task 3, add:

```xml
    <!-- ─── Import Warnings dialog ──────────────────────────────────────── -->
    <sys:String x:Key="ImportWarnings_Title">Import Warnings</sys:String>
    <sys:String x:Key="ImportWarnings_Body">Some constructs in this file could not be imported and were skipped. The conversation structure was imported, but the following were dropped:</sys:String>
    <sys:String x:Key="ImportWarnings_Footer">Open the original file to handle these manually if needed.</sys:String>
    <sys:String x:Key="ImportWarnings_OccurrenceSuffix">occurrence(s)</sys:String>
    <sys:String x:Key="ImportWarnings_Ok">OK</sys:String>
```

- [ ] **Step 2: Write the failing headless dialog tests**

Create `DialogEditor.Tests/Views/ImportWarningsDialogTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Import;

namespace DialogEditor.Tests.Views;

public class ImportWarningsDialogTests
{
    private static readonly IReadOnlyList<ImportWarning> Warnings =
    [
        new("if", 2),
        new("set", 1),
    ];

    [AvaloniaFact]
    public void Dialog_ListsOneRowPerWarning()
    {
        var dialog = new ImportWarningsDialog(Warnings);
        dialog.Show();
        Assert.Equal(Warnings.Count, dialog.FindControl<ItemsControl>("WarningsList")!.ItemCount);
    }

    [AvaloniaFact]
    public void OkButton_ClosesDialog()
    {
        var dialog = new ImportWarningsDialog(Warnings);
        dialog.Show();
        dialog.FindControl<Button>("OkButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(dialog.IsVisible);
    }
}
```

- [ ] **Step 3: Run the tests — verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ImportWarningsDialogTests"`
Expected: FAIL to compile — `ImportWarningsDialog` does not exist.

- [ ] **Step 4: Create the dialog XAML**

Create `DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.ImportWarningsDialog"
        Title="{StaticResource ImportWarnings_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="440" SizeToContent="Height" MaxHeight="520"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Background="#252525"
        x:CompileBindings="False">

    <StackPanel Margin="20">

        <TextBlock Text="{StaticResource ImportWarnings_Body}"
                   Foreground="#e8e8e8" FontSize="12"
                   TextWrapping="Wrap" Margin="0,0,0,12"/>

        <Border BorderBrush="#444" BorderThickness="1" CornerRadius="4"
                MaxHeight="280" Margin="0,0,0,12">
            <ScrollViewer>
                <ItemsControl x:Name="WarningsList">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding}"
                                       Foreground="#ddd" FontSize="12"
                                       Padding="8,4"/>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </Border>

        <TextBlock Text="{StaticResource ImportWarnings_Footer}"
                   Foreground="#888" FontSize="11"
                   TextWrapping="Wrap" Margin="0,0,0,16"/>

        <Button x:Name="OkButton"
                Content="{StaticResource ImportWarnings_Ok}"
                HorizontalAlignment="Right"
                Background="#1a5276" Foreground="White" BorderThickness="0"
                Padding="20,6" FontSize="12"/>

    </StackPanel>

</Window>
```

> The OK button is the documented self-explanatory exception, so no `ToolTip.Tip` is required on it.

- [ ] **Step 5: Create the code-behind**

Create `DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml.cs`:

```csharp
using Avalonia;
using Avalonia.Controls;
using DialogEditor.Core.Import;

namespace DialogEditor.Avalonia.Views;

public partial class ImportWarningsDialog : Window
{
    // Parameterless constructor required so the Avalonia XAML compiler can complete
    // first-pass type analysis (otherwise AVLN3000 blocks App.axaml compilation).
    public ImportWarningsDialog() => InitializeComponent();

    public ImportWarningsDialog(IReadOnlyList<ImportWarning> warnings)
    {
        InitializeComponent();

        var suffix = Application.Current!.FindResource("ImportWarnings_OccurrenceSuffix") as string
                     ?? "occurrence(s)";
        WarningsList.ItemsSource = warnings
            .Select(w => $"<<{w.Construct}>> — {w.Count} {suffix}")
            .ToList();

        OkButton.Click += (_, _) => Close();
    }
}
```

- [ ] **Step 6: Run the tests — verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ImportWarningsDialogTests"`
Expected: PASS.

> If the **first** build after creating the new AXAML file fails with `AVLN3000` ("Unable to find public constructor"), run the build a second time — this is the documented first-pass cache miss. If it persists, delete `DialogEditor.Avalonia/obj/Debug/net8.0/Avalonia/resources` and rebuild.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/Views/ImportWarningsDialogTests.cs
git commit -m "feat: ImportWarningsDialog with headless tests"
```

---

### Task 5: Wire the dialog into MainWindow + remove the gap

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs:47`
- Modify: `Gaps.md`

- [ ] **Step 1: Wire the `ShowImportWarnings` callback**

In `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`, in the constructor where the other callbacks are assigned (after the `vm.RequestConflictResolution = ...` / `vm.ShowExportConversations = ...` block, around line 47-53), add:

```csharp
        vm.ShowImportWarnings = async warnings =>
        {
            var dialog = new ImportWarningsDialog(warnings);
            await dialog.ShowDialog(this);
        };
```

- [ ] **Step 2: Build the app to confirm wiring compiles**

Run: `dotnet build DialogEditor.Avalonia`
Expected: `Build succeeded.` with 0 errors. (Pre-existing `AVLN3001` warnings about windows without public constructors are unrelated and acceptable.)

- [ ] **Step 3: Remove the Yarn-import gap from `Gaps.md`**

In `Gaps.md`, delete the entire `### Yarn Spinner Import — Conditions and Commands` section (the heading and its paragraph). Leave surrounding sections intact.

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — all tests green (789 prior + the new tests added in Tasks 1–4).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs Gaps.md
git commit -m "feat: wire ImportWarningsDialog into import flow; close Yarn-import gap"
```

---

## Final Verification

1. `dotnet test DialogEditor.Tests` — all tests pass.
2. `dotnet build DialogEditor.Avalonia` — 0 errors.
3. Manual: File → Import Conversation, pick a `.yarn` file containing `<<if>>`/`<<set>>` — the ImportWarningsDialog appears listing the skipped constructs with counts; after dismissing it, the status bar shows `Imported '<name>' (N nodes). Skipped Yarn constructs: <<if>>, <<set>>.`
4. Manual: import a `.yarn` file with only plain dialogue — no dialog, status bar shows the clean `Imported '<name>' (N nodes).`
5. Manual: import a `.csv` and a `.json` — no dialog, clean status message.
