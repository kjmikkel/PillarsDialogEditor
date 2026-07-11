# Validate Text Tags — Project-Wide Sweep Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A **Test ▸ Validate Text Tags…** window that scans every patched conversation's saved text (all languages) for token typos and unbalanced markup, with a three-way dirty guard (Save and scan / Scan saved state only / Cancel).

**Architecture:** Pure `ProjectTextTagScanner` walks `DialogProject.Patches[*].Translations` (plus a defensive `AddedNodes` text check) through the existing `TokenValidationService` — all in-memory, zero IO, synchronous. `TextTagValidationViewModel` + `TextTagValidationWindow` mirror the VoValidation pattern; `MainWindowViewModel.RequestTextTagValidationAsync()` handles the dirty guard behind an injectable seam mirroring `ConfirmSaveBeforeApply`.

**Tech Stack:** C# / .NET 8, Avalonia, xUnit, CommunityToolkit.Mvvm. No new dependencies.

## Global Constraints

- **TDD, red first.** Tests in `DialogEditor.Tests`; suite runs serially.
- **Text location (verified in `DiffEngine.cs:21,69`):** all dialog text lives in `patch.Translations[lang]`; `AddedNodes` text is zeroed at save; `FieldChanges` never holds `DefaultText`/`FemaleText`. The scanner walks Translations + a defensive non-empty AddedNodes check. **No JSON decoding.**
- **Primary-language mapping:** rows whose language equals the scanner's `primaryLanguage` argument get `Language == ""` (displayed as "Default"); pass `_provider?.Language ?? ""` from the VM factory.
- **Messages** reuse the existing `Validation_UnknownToken_Suggest` / `Validation_UnknownToken` / `Validation_UnbalancedMarkup` keys via `Loc.Format` — identical to the detail panel / Flow Analytics.
- **Localisation:** every new user-visible string is a `sys:String` in `DialogEditor.Avalonia/Resources/Strings.axaml`; tooltips + mirrored `AutomationProperties.HelpText` on interactive controls (enforced by `AutomationHelpTextTests`); no stray hex (existing `Brush.*` tokens only).
- **Error handling:** scanner pure/non-throwing; no bare catch; `AppLog` on caught exceptions.
- **Build/test:** `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj` (filter: `--filter "FullyQualifiedName~<name>"`).

---

### Task 1: `ProjectTextTagScanner`

**Files:**
- Create: `DialogEditor.ViewModels/Services/ProjectTextTagScanner.cs`
- Test: `DialogEditor.Tests/Services/ProjectTextTagScannerTests.cs` (create)

**Interfaces:**
- Consumes: `DialogProject` (`DialogEditor.Patch`; `Patches` is a `Dictionary<string, ConversationPatch>`), `ConversationPatch.Translations`, `NodeTranslation(NodeId, DefaultText, FemaleText)`, `TokenValidationService.Validate(text, gameId)` → `TokenIssue(Kind, Fragment, Suggestion, Position)`.
- Produces:
  - `public sealed record TextTagIssueRow(string ConversationName, int NodeId, string Language, string Message);`
  - `public static class ProjectTextTagScanner { public static IReadOnlyList<TextTagIssueRow> Scan(DialogProject project, string gameId, string primaryLanguage, TokenValidationService? validator = null); }`
  - Ordering: conversation name (ordinal), then node id, then language (`""` first, then ordinal).

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Services/ProjectTextTagScannerTests.cs`. Check `DialogEditor.Patch.DialogProject` for its construction (`DialogProject.Empty(name)` exists; `Patches` mutable dictionary — copy how other tests build a project with patches, e.g. `MainWindowViewModelPersistenceTests` or `VoOrphanScanner` tests; use the real `ConversationPatch` constructor: `(ConversationName, SchemaVersion, AddedNodes, DeletedNodeIds, ModifiedNodes) { Translations = … }`).

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ProjectTextTagScannerTests
{
    public ProjectTextTagScannerTests() => Loc.Configure(new StubStringProvider());

    private static ConversationPatch Patch(
        string name,
        IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> translations)
        => new(name, ConversationPatch.CurrentSchemaVersion, [], [], [])
           { Translations = translations };

    private static DialogProject Project(params ConversationPatch[] patches)
    {
        var project = DialogProject.Empty("Test");
        foreach (var p in patches) project.Patches[p.ConversationName] = p;
        return project;
    }

    [Fact]
    public void PrimaryLanguageTypo_ReportedAsDefault()
    {
        var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["en"] = [new NodeTranslation(5, "Hi [Player Nmae]", "")],
        }));
        var row = Assert.Single(ProjectTextTagScanner.Scan(project, "poe2", "en"));
        Assert.Equal(("conv_a", 5, ""), (row.ConversationName, row.NodeId, row.Language));
        Assert.Equal("Validation_UnknownToken_Suggest", row.Message); // stub echoes the key
    }

    [Fact]
    public void TranslationTypo_CarriesLanguageCode()
    {
        var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["fr"] = [new NodeTranslation(7, "Bonjour [Player Nmae]", "")],
        }));
        var row = Assert.Single(ProjectTextTagScanner.Scan(project, "poe2", "en"));
        Assert.Equal("fr", row.Language);
    }

    [Fact]
    public void FemaleText_Validated()
    {
        var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["en"] = [new NodeTranslation(3, "clean", "she says <i>oops")],
        }));
        var row = Assert.Single(ProjectTextTagScanner.Scan(project, "poe2", "en"));
        Assert.Equal("Validation_UnbalancedMarkup", row.Message);
    }

    [Fact]
    public void CleanProject_Empty()
    {
        var project = Project(Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["en"] = [new NodeTranslation(1, "Hello [Player Name].", "")],
        }));
        Assert.Empty(ProjectTextTagScanner.Scan(project, "poe2", "en"));
    }

    [Fact]
    public void Rows_OrderedByConversationNodeLanguage()
    {
        var project = Project(
            Patch("conv_b", new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(2, "[Player Nmae]", "")],
            }),
            Patch("conv_a", new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["fr"] = [new NodeTranslation(9, "[Player Nmae]", "")],
                ["en"] = [new NodeTranslation(9, "[Player Nmae]", "")],
            }));
        var rows = ProjectTextTagScanner.Scan(project, "poe2", "en");
        Assert.Equal(
            [("conv_a", 9, ""), ("conv_a", 9, "fr"), ("conv_b", 2, "")],
            rows.Select(r => (r.ConversationName, r.NodeId, r.Language)).ToList());
    }

    [Fact]
    public void DefensiveAddedNodesText_ValidatedWhenPresent()
    {
        // Current schema zeroes AddedNodes text; a legacy patch might not.
        var node = new DialogEditor.Core.Editing.NodeEditSnapshot(
            4, false, SpeakerCategory.Npc, "", "", "legacy [Player Nmae]", "",
            "Conversation", "None", "", "", "", false, false, [], [], []);
        var patch = new ConversationPatch("conv_a", 1, [node], [], []);
        var project = Project(patch);
        var row = Assert.Single(ProjectTextTagScanner.Scan(project, "poe2", "en"));
        Assert.Equal(4, row.NodeId);
        Assert.Equal("", row.Language);
    }
}
```

If `DialogProject.Empty(...)`/`Patches` differ from this shape, mirror the real API from `DialogEditor.Patch/DialogProject.cs` — do not invent members.

- [ ] **Step 2: Run to verify failure**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~ProjectTextTagScannerTests"`
Expected: FAIL — `ProjectTextTagScanner` missing (compile error).

- [ ] **Step 3: Implement**

Create `DialogEditor.ViewModels/Services/ProjectTextTagScanner.cs`:

```csharp
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// One project-wide sweep result: a token/markup problem in writer-touched text.
/// Language is "" for the primary language (displayed as "Default").
public sealed record TextTagIssueRow(
    string ConversationName, int NodeId, string Language, string Message);

/// Scans every saved patch's text (all languages) for token typos and unbalanced
/// markup. Pure and IO-free: DiffEngine stores ALL dialog text in
/// patch.Translations[lang] (AddedNodes text is zeroed at save; FieldChanges never
/// holds dialog text — DiffEngine.cs:21,69), so the walk is Translations plus a
/// defensive AddedNodes check for legacy patches.
/// Spec: docs/superpowers/specs/2026-07-09-text-tag-project-sweep-design.md
public static class ProjectTextTagScanner
{
    public static IReadOnlyList<TextTagIssueRow> Scan(
        DialogProject project, string gameId, string primaryLanguage,
        TokenValidationService? validator = null)
    {
        validator ??= new TokenValidationService();
        var rows = new List<TextTagIssueRow>();

        foreach (var (convName, patch) in project.Patches)
        {
            if (patch.IsEmpty) continue;

            foreach (var (lang, entries) in patch.Translations)
            {
                var label = string.Equals(lang, primaryLanguage, StringComparison.OrdinalIgnoreCase)
                    ? "" : lang;
                foreach (var t in entries)
                {
                    Append(convName, t.NodeId, label, t.DefaultText);
                    Append(convName, t.NodeId, label, t.FemaleText);
                }
            }

            // Defensive: current schema zeroes AddedNodes text, but a legacy or
            // hand-edited patch may still carry it.
            foreach (var n in patch.AddedNodes)
            {
                Append(convName, n.NodeId, "", n.DefaultText);
                Append(convName, n.NodeId, "", n.FemaleText);
            }
        }

        return rows
            .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
            .ThenBy(r => r.NodeId)
            .ThenBy(r => r.Language, StringComparer.Ordinal)
            .ToList();

        void Append(string conv, int nodeId, string lang, string? text)
        {
            if (string.IsNullOrEmpty(text)) return;
            foreach (var issue in validator!.Validate(text, gameId))
            {
                var msg = issue.Kind switch
                {
                    TokenIssueKind.UnbalancedMarkup =>
                        Loc.Format("Validation_UnbalancedMarkup", issue.Fragment),
                    _ when issue.Suggestion is not null =>
                        Loc.Format("Validation_UnknownToken_Suggest", issue.Fragment, issue.Suggestion),
                    _ => Loc.Format("Validation_UnknownToken", issue.Fragment),
                };
                rows.Add(new TextTagIssueRow(conv, nodeId, lang, msg));
            }
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~ProjectTextTagScannerTests"`
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/ProjectTextTagScanner.cs DialogEditor.Tests/Services/ProjectTextTagScannerTests.cs
git commit -m "feat(validation): project-wide text-tag scanner"
```

---

### Task 2: `TextTagValidationViewModel` + strings

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/ViewModels/TextTagValidationViewModelTests.cs` (create)

**Interfaces:**
- Consumes: `TextTagIssueRow` (Task 1).
- Produces: `TextTagValidationViewModel(Func<IReadOnlyList<TextTagIssueRow>> scan)` with:
  - `IReadOnlyList<TextTagRowViewModel> Rows` (row VM: `NodeLabel` ("Node 5"), `LanguageLabel` ("Default" / "fr"), `Message`, `ConversationName`)
  - `string SummaryText` (localized: "N issues across M conversations" / none)
  - `bool HasIssues`, `RefreshCommand` (re-invokes the scan delegate)

- [ ] **Step 1: Add strings**

In `Strings.axaml`, near the `VoValidation_*` block:

```xml
    <sys:String x:Key="TextTagValidation_Title">Validate Text Tags</sys:String>
    <sys:String x:Key="TextTagValidation_SavedNote">Scans the saved project — text in every patched conversation and every language.</sys:String>
    <sys:String x:Key="TextTagValidation_Summary">{0} issue(s) across {1} conversation(s)</sys:String>
    <sys:String x:Key="TextTagValidation_NoIssues">No text tag issues found.</sys:String>
    <sys:String x:Key="TextTagValidation_Default">Default</sys:String>
    <sys:String x:Key="Menu_ValidateTextTags">Validate Text Tags…</sys:String>
    <sys:String x:Key="ToolTip_ValidateTextTags">Scan every patched conversation in the saved project (all languages) for unknown substitution tokens and unbalanced markup. Requires an open project.</sys:String>
    <sys:String x:Key="ToolTip_TextTagValidation_Refresh">Re-scan the saved project for text tag issues</sys:String>
```

**Note on `TextTagValidation_Summary`:** check `NoNaivePluralTests` — the "(s)" idiom is banned. Use `Loc.FormatCount` with plural keys instead:

```xml
    <sys:String x:Key="TextTagValidation_Summary_One">{0} issue across {1} conversation(s)</sys:String>
```

is still wrong — compose two pre-pluralised fragments as the pluralisation spec does. Concretely:

```xml
    <sys:String x:Key="TextTagValidation_Issues_One">{0} issue</sys:String>
    <sys:String x:Key="TextTagValidation_Issues_Other">{0} issues</sys:String>
    <sys:String x:Key="TextTagValidation_Convs_One">{0} conversation</sys:String>
    <sys:String x:Key="TextTagValidation_Convs_Other">{0} conversations</sys:String>
    <sys:String x:Key="TextTagValidation_Summary">{0} across {1}</sys:String>
```

and build `SummaryText` as `Loc.Format("TextTagValidation_Summary", Loc.FormatCount("TextTagValidation_Issues", issueCount), Loc.FormatCount("TextTagValidation_Convs", convCount))` — mirroring the existing two-count composition pattern from the pluralisation work.

- [ ] **Step 2: Write the failing tests**

```csharp
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class TextTagValidationViewModelTests
{
    public TextTagValidationViewModelTests() => Loc.Configure(new StubStringProvider());

    private static TextTagIssueRow Row(string conv, int node, string lang) =>
        new(conv, node, lang, "msg");

    [Fact]
    public void Rows_PopulatedFromScan_WithLabels()
    {
        var vm = new TextTagValidationViewModel(() => [Row("conv_a", 5, ""), Row("conv_a", 5, "fr")]);
        Assert.Equal(2, vm.Rows.Count);
        Assert.Equal("TextTagValidation_Default", vm.Rows[0].LanguageLabel); // stub echoes key
        Assert.Equal("fr", vm.Rows[1].LanguageLabel);
        Assert.True(vm.HasIssues);
    }

    [Fact]
    public void EmptyScan_NoIssues()
    {
        var vm = new TextTagValidationViewModel(() => []);
        Assert.False(vm.HasIssues);
        Assert.Empty(vm.Rows);
    }

    [Fact]
    public void Refresh_ReinvokesScan()
    {
        var results = new List<TextTagIssueRow>();
        var vm = new TextTagValidationViewModel(() => results);
        Assert.Empty(vm.Rows);
        results.Add(Row("conv_a", 1, ""));
        vm.RefreshCommand.Execute(null);
        Assert.Single(vm.Rows);
    }
}
```

- [ ] **Step 3: Run to verify failure, then implement**

Run the filter; expect compile failure. Then create `TextTagValidationViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public sealed class TextTagRowViewModel
{
    public string ConversationName { get; }
    public string NodeLabel        { get; }
    public string LanguageLabel    { get; }
    public string Message          { get; }

    public TextTagRowViewModel(TextTagIssueRow row)
    {
        ConversationName = row.ConversationName;
        NodeLabel        = $"Node {row.NodeId}";
        LanguageLabel    = row.Language.Length == 0
            ? Loc.Get("TextTagValidation_Default") : row.Language;
        Message          = row.Message;
    }
}

/// Project-wide text-tag validation results (Test ▸ Validate Text Tags…). The scan
/// delegate reads the CURRENT saved project each invocation, so Refresh picks up
/// saves made while the window is open.
public partial class TextTagValidationViewModel : ObservableObject
{
    private readonly Func<IReadOnlyList<TextTagIssueRow>> _scan;

    public ObservableCollection<TextTagRowViewModel> Rows { get; } = [];

    [ObservableProperty] private string _summaryText = string.Empty;
    [ObservableProperty] private bool   _hasIssues;

    public TextTagValidationViewModel(Func<IReadOnlyList<TextTagIssueRow>> scan)
    {
        _scan = scan;
        Refresh();
    }

    [RelayCommand]
    private void Refresh()
    {
        var rows = _scan();
        Rows.Clear();
        foreach (var r in rows) Rows.Add(new TextTagRowViewModel(r));
        HasIssues = rows.Count > 0;
        var convCount = rows.Select(r => r.ConversationName).Distinct().Count();
        SummaryText = rows.Count == 0
            ? Loc.Get("TextTagValidation_NoIssues")
            : Loc.Format("TextTagValidation_Summary",
                Loc.FormatCount("TextTagValidation_Issues", rows.Count),
                Loc.FormatCount("TextTagValidation_Convs", convCount));
    }
}
```

(`NodeLabel`'s "Node {id}" — check whether a localized `Label_Node`-style resource exists; if `VoValidationIssue.NodeLabel` uses a resource, mirror it; a bare interpolation of a number with the English word "Node" would violate the localisation rule, so reuse whatever key VoValidation uses, or add `TextTagValidation_NodeLabel` = `Node {0}`.)

- [ ] **Step 4: Run to verify pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~TextTagValidationViewModelTests"` → PASS.
Also run `--filter "FullyQualifiedName~NoNaivePluralTests"` → PASS (no "(s)" idiom introduced).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/TextTagValidationViewModel.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/TextTagValidationViewModelTests.cs
git commit -m "feat(validation): text-tag validation view-model + strings"
```

---

### Task 3: Dirty guard + window + menu

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (seam + factory + request method)
- Create: `DialogEditor.Avalonia/Views/TextTagValidationWindow.axaml` + `.axaml.cs`
- Create: `DialogEditor.Avalonia/Views/SaveBeforeScanDialog.axaml` + `.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (menu item) + `MainWindow.axaml.cs` (handler, cached window)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelTextTagTests.cs` (create)

**Interfaces:**
- Consumes: `ProjectTextTagScanner.Scan` (Task 1), `TextTagValidationViewModel` (Task 2), existing `IsModified`, `SaveProject()`, `_project`, `_activeGameId`, `_provider`.
- Produces on `MainWindowViewModel`:
  - `public enum ScanDirtyChoice { SaveAndScan, ScanSavedOnly, Cancel }` (own file or nested — put it in `DialogEditor.ViewModels/Services/ScanDirtyChoice.cs`)
  - `public Func<Task<ScanDirtyChoice>>? ConfirmScanWithUnsavedChanges { get; set; }`
  - `public async Task<TextTagValidationViewModel?> RequestTextTagValidationAsync()`

- [ ] **Step 1: Write the failing VM tests**

Create `MainWindowViewModelTextTagTests.cs` — reuse the `MainWindowViewModelTests` isolation (settings-path override) and `MakeVm()`-style construction (`new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker())`). To make the VM dirty/have a project, use the same reflection `InjectProject` helper pattern `MainWindowViewModelTests` uses (copy it) and whatever existing mechanism marks `IsModified` (inspect the property: if it derives from canvas/project state, set it the way existing tests do — search `IsModified` usage in tests first and mirror; if no test sets it, add the dirty case via the canvas edit route used elsewhere).

```csharp
    [Fact]
    public async Task NoProject_ReturnsNull()
    {
        var vm = MakeVm();
        Assert.Null(await vm.RequestTextTagValidationAsync());
    }

    [Fact]
    public async Task CleanProject_ReturnsVm_WithoutConsultingSeam()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        var consulted = false;
        vm.ConfirmScanWithUnsavedChanges = () => { consulted = true; return Task.FromResult(ScanDirtyChoice.Cancel); };
        var result = await vm.RequestTextTagValidationAsync();
        Assert.NotNull(result);
        Assert.False(consulted);
    }

    [Fact]
    public async Task Dirty_Cancel_ReturnsNull()   { /* dirty vm; seam returns Cancel; assert null, save not called */ }

    [Fact]
    public async Task Dirty_ScanSavedOnly_ReturnsVm_WithoutSave() { /* seam returns ScanSavedOnly; assert vm returned */ }

    [Fact]
    public async Task Dirty_SaveAndScan_SavesThenReturnsVm()      { /* seam returns SaveAndScan; assert saved (project file written / IsModified false) then vm */ }
```

Flesh the three dirty cases out against the real `IsModified` mechanics found in step-1 exploration — assert save via the observable the codebase offers (e.g. `IsModified` flips false, or the project file exists at the injected `_projectPath`). Do not weaken to "doesn't throw".

- [ ] **Step 2: Run to verify failure**

Filter `MainWindowViewModelTextTagTests` → FAIL (missing members).

- [ ] **Step 3: Implement the VM side**

`DialogEditor.ViewModels/Services/ScanDirtyChoice.cs`:

```csharp
namespace DialogEditor.ViewModels.Services;

/// User's choice when starting a project-wide scan with unsaved changes.
public enum ScanDirtyChoice { SaveAndScan, ScanSavedOnly, Cancel }
```

On `MainWindowViewModel` (near `ConfirmSaveBeforeApply`):

```csharp
    /// Seam for the Validate Text Tags dirty guard (three-way consent). Null in
    /// unit tests that don't exercise the dialog; the View wires SaveBeforeScanDialog.
    public Func<Task<ScanDirtyChoice>>? ConfirmScanWithUnsavedChanges { get; set; }

    /// Test ▸ Validate Text Tags…: returns a ready view-model over the SAVED project,
    /// or null when no project is open / the user cancels the dirty guard.
    public async Task<TextTagValidationViewModel?> RequestTextTagValidationAsync()
    {
        if (_project is null) return null;

        if (IsModified)
        {
            var choice = ConfirmScanWithUnsavedChanges is null
                ? ScanDirtyChoice.Cancel
                : await ConfirmScanWithUnsavedChanges();
            if (choice == ScanDirtyChoice.Cancel) return null;
            if (choice == ScanDirtyChoice.SaveAndScan) SaveProject();
        }

        // Closure reads the current fields so Refresh in the open window picks up
        // later saves.
        return new TextTagValidationViewModel(() =>
            _project is null
                ? []
                : ProjectTextTagScanner.Scan(_project, _activeGameId, _provider?.Language ?? ""));
    }
```

(Verify `SaveProject()` is the right save entry point — it's what the `ConfirmSaveBeforeApply` path calls at line ~1125.)

- [ ] **Step 4: Run VM tests → PASS, then build the windows**

`SaveBeforeScanDialog.axaml` — mirror an existing small dialog (`ForceDeleteDialog.axaml` is the closest three-of-a-kind: title, message, button row). Three buttons — Save and scan / Scan saved state only / Cancel — each with tooltip + HelpText; code-behind exposes `public Task<ScanDirtyChoice> ShowFor(Window owner)` or sets a `Choice` property closed with `ShowDialog<ScanDirtyChoice>` (Avalonia supports typed dialog results — mirror how `ForceDeleteDialog`/`CommitConsentDialog` return results and do the same). Strings:

```xml
    <sys:String x:Key="SaveBeforeScan_Title">Unsaved changes</sys:String>
    <sys:String x:Key="SaveBeforeScan_Message">The scan reads the saved project. Unsaved changes will not be included.</sys:String>
    <sys:String x:Key="SaveBeforeScan_SaveAndScan">Save and scan</sys:String>
    <sys:String x:Key="SaveBeforeScan_ScanSavedOnly">Scan saved state only</sys:String>
    <sys:String x:Key="ToolTip_SaveBeforeScan_SaveAndScan">Save the project first, then scan everything including your latest edits</sys:String>
    <sys:String x:Key="ToolTip_SaveBeforeScan_ScanSavedOnly">Scan the project as last saved; unsaved edits are not included</sys:String>
```

(`Button_Cancel` already exists.)

`TextTagValidationWindow.axaml` — mirror `VoValidationWindow.axaml`'s skeleton (title/icon/size/`Brush.Surface.Window`/CenterOwner/`x:CompileBindings="False"`; grid of summary + saved-state note + Refresh button + results `ItemsControl` + `FocusHintBar`). Row template: conversation (bold, `Brush.Text.Emphasis`), `NodeLabel` (muted), `LanguageLabel` (tertiary, fixed width), `Message` (wrapping, `Brush.Text.Secondary`). Empty state bound to `!HasIssues` shows `TextTagValidation_NoIssues`. Refresh button carries `ToolTip_TextTagValidation_Refresh` + HelpText. Window `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"` (house rule).

`MainWindow.axaml` — after the Validate Voice-Over item:

```xml
                        <MenuItem Header="{DynamicResource Menu_ValidateTextTags}"
                                  Click="ValidateTextTags_Click"
                                  IsEnabled="{Binding IsProjectOpen}"
                                  ToolTip.Tip="{DynamicResource ToolTip_ValidateTextTags}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_ValidateTextTags}"/>
```

`MainWindow.axaml.cs` — cached-window handler mirroring `ValidateVo_Click`, wiring the seam once (e.g. in the same wiring block as `ShowChangelog`):

```csharp
        vm.ConfirmScanWithUnsavedChanges = async () =>
            await new SaveBeforeScanDialog().ShowFor(this);
```

```csharp
    private async void ValidateTextTags_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            if (_textTagValidationWindow is { IsVisible: true })
            {
                _textTagValidationWindow.Activate();
                return;
            }
            var vm = await ((MainWindowViewModel)DataContext!).RequestTextTagValidationAsync();
            if (vm is null) return;
            _textTagValidationWindow = new TextTagValidationWindow(vm);
            _textTagValidationWindow.Closed += (_, _) => _textTagValidationWindow = null;
            _textTagValidationWindow.Show(this);
        }
        catch (Exception ex)
        {
            AppLog.Error("Validate Text Tags failed", ex);
        }
    }
```

(async void handler → wrap in try/catch with AppLog per the error-handling rule; check how other async handlers in this file do it and match.)

- [ ] **Step 5: Full suite + headless window test**

Add a headless `[AvaloniaFact]` (mirroring `ChangelogWindowTests`) constructing `TextTagValidationWindow` with a populated and an empty VM, `Show()`, assert `IsVisible`. Run the full suite:
`dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj` → PASS (incl. `AutomationHelpTextTests`, `NoNaivePluralTests`, `NoStrayHexTests` against the new XAML).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels DialogEditor.Avalonia DialogEditor.Tests
git commit -m "feat(validation): Validate Text Tags window, menu, and dirty guard"
```

---

### Task 4: App verification + Gaps.md

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Verify in the running app**

`running-the-app` skill (backup → scratch project → restore). The scratch project has no patches, so the flow to verify is chrome + gating: Test menu shows **Validate Text Tags…** (enabled with the project open); clicking opens the window with the "no issues" empty state and the saved-note caption; Refresh works; window closes/reopens cleanly. Screenshot the window. (A populated scan is covered by unit tests; constructing a dirty project via UIA is not reliable — note this.)

- [ ] **Step 2: Update `Gaps.md`**

In "Token autocomplete and validation in node text editing", update the Validation bullet's closing sentence ("Per-conversation scope; a project-wide translation sweep is deferred.") to:

```markdown
  Project-wide sweep ✅ implemented (2026-07-09): **Test ▸ Validate Text Tags…** scans
  every patched conversation's saved text in every language (pure
  `ProjectTextTagScanner` over `DialogProject.Patches[*].Translations` — zero IO),
  with a three-way dirty guard (save and scan / scan saved state only / cancel)
  shown only when the project has unsaved changes. Spec:
  docs/superpowers/specs/2026-07-09-text-tag-project-sweep-design.md.
```

- [ ] **Step 3: Full suite + commit**

`dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj` → PASS.

```bash
git add Gaps.md
git commit -m "docs(gaps): project-wide text-tag sweep implemented"
```

---

## Self-Review

**Spec coverage:** scanner incl. the DiffEngine correction (Translations walk, defensive AddedNodes, primary-language mapping, ordering) → Task 1; VM + summary/empty state + Refresh → Task 2; dirty guard (three-way seam, dialog, only-when-dirty), window, menu gating, factory → Task 3; verification + Gaps.md → Task 4. Cross-cutting: localisation incl. pluralisation compliance (Task 2 note), tooltips/HelpText (Tasks 2–3 + enforcer tests in Task 3 step 5), error handling (async handler try/catch + pure scanner), serial tests via seams. ✔

**Placeholder scan:** the three dirty-case test bodies in Task 3 step 1 are sketched with explicit assertions to make against the real `IsModified` mechanics — with the instruction to mirror how existing tests drive `IsModified` and to not weaken assertions; all other steps carry complete code. ✔

**Type consistency:** `TextTagIssueRow(ConversationName, NodeId, Language, Message)` and `Scan(project, gameId, primaryLanguage, validator?)` match across Tasks 1–3; `ScanDirtyChoice` values match between enum, seam, and tests; `TextTagValidationViewModel(Func<IReadOnlyList<TextTagIssueRow>>)` matches Tasks 2–3; string keys used in code exist in Task 2's additions. ✔
