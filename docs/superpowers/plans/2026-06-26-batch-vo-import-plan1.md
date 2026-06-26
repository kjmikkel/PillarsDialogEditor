# Batch VO Import — Plan 1: Shared Dialog + Conversation-Level Entry Point

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Batch Import Voice-Over…" entry to the canvas context menu that opens a scrollable table of every VO node in the current conversation, letting the user browse for source files per row and import them all in one pass.

**Architecture:** `BatchVoImportViewModel` + `BatchVoRowViewModel` live in `DialogEditor.ViewModels`; `BatchVoImportDialog` lives in `DialogEditor.Avalonia/Views`; the entry point is a new `BatchImportVoCommand` on `ConversationViewModel` wired via a `ShowBatchVoImport` delegate set in `MainWindow.axaml.cs`. This mirrors the existing per-node import pattern exactly.

**Tech Stack:** C# 12 / .NET 8, Avalonia 11, CommunityToolkit.Mvvm source generators, NAudio, xUnit, `StubDispatcher` + `StubStringProvider` from `DialogEditor.Tests.Helpers`.

## Global Constraints

- No user-visible text hard-coded in XAML or C# — all strings in `Strings.axaml`
- Every interactive control must have `ToolTip.Tip` + `AutomationProperties.HelpText`
- Every `Window` must have `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`
- All caught exceptions logged via `AppLog.Error`/`AppLog.Warn`; `OperationCanceledException` swallowed silently; no bare `catch {}`
- Strict red/green TDD: failing test before implementation code
- Tests run serially: `dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false`

---

## File Map

| Action | File | Responsibility |
|--------|------|---------------|
| Create | `DialogEditor.ViewModels/ViewModels/BatchVoImportViewModel.cs` | `BatchRowStatus` enum + `BatchVoRowViewModel` + `BatchVoImportViewModel` |
| Create | `DialogEditor.Tests/ViewModels/BatchVoImportViewModelTests.cs` | 5 ViewModel tests |
| Modify | `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` | Add `public void Refresh()` wrapper |
| Modify | `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs` | Add `ProjectPath`, `ShowBatchVoImport`, `BatchImportVoCommand`, `CanBatchImportVo`, `BuildBatchVoRows` |
| Modify | `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Sync `Canvas.ProjectPath` alongside `Detail.ProjectPath` (3 sites) |
| Create | `DialogEditor.Tests/ViewModels/ConversationViewModelBatchVoTests.cs` | 2 BuildBatchVoRows tests |
| Modify | `DialogEditor.Avalonia/Resources/Strings.axaml` | Add all `BatchVoImport_*` + `Menu_BatchImportVo` keys |
| Create | `DialogEditor.Avalonia/Views/BatchVoImportDialog.axaml` | Dialog UI: header, DataGrid, footer |
| Create | `DialogEditor.Avalonia/Views/BatchVoImportDialog.axaml.cs` | Code-behind: Browse/Play/Clear/Import/Cancel handlers |
| Modify | `DialogEditor.Avalonia/Views/ConversationView.axaml` | Add "Batch Import Voice-Over…" to canvas context menu |
| Modify | `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Wire `Canvas.ShowBatchVoImport` delegate |

---

### Task 1: BatchVoRowViewModel + BatchVoImportViewModel

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/BatchVoImportViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/BatchVoImportViewModelTests.cs`

**Interfaces:**
- Consumes: `IVoImporter`, `VoImportRequest`, `VoImportResult`, `WemQuality` (all in `DialogEditor.ViewModels.Services`), `VoPresence` (from `VoCheckResult.cs`)
- Produces:
  - `BatchRowStatus` enum: `Pending, Importing, Done, Error`
  - `BatchVoRowViewModel(string conversationName, int nodeId, string textPreview, VoPresence voStatus, string destPrimaryPath, string destFemPath)` — observable `PrimarySourcePath`, `FemSourcePath`, `RowStatus`, `ErrorMessage`, computed `HasPrimarySource`, `PrimaryFileLabel`, `FemFileLabel`, `VoStatusGlyph`, `RowStatusGlyph`, `IsPlayingPrimary`, `IsPlayingFem`, `PrimaryPlayGlyph`, `FemPlayGlyph`
  - `BatchVoImportViewModel(IReadOnlyList<BatchVoRowViewModel> rows, IVoImporter importer)` — `AllRows`, `VisibleRows`, `ShowOnlyMissing`, `Quality`, `IsImporting`, `ProgressText`, `ImportCommand`, `void Cancel()`, `void OnRowChanged()`

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/BatchVoImportViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class BatchVoImportViewModelTests
{
    public BatchVoImportViewModelTests() => Loc.Configure(new StubStringProvider());

    private static BatchVoRowViewModel MakeRow(
        VoPresence status = VoPresence.Missing,
        string? primarySrc = null) =>
        new("conv", 1, "hello", status, "C:/dest/a.wem", "C:/dest/a_fem.wem")
            { PrimarySourcePath = primarySrc };

    // ── ShowOnlyMissing ──────────────────────────────────────────────────

    [Fact]
    public void ShowOnlyMissing_True_ExcludesFoundRows()
    {
        var found   = MakeRow(VoPresence.Found);
        var missing = MakeRow(VoPresence.Missing);
        var vm = new BatchVoImportViewModel([found, missing], new StubImporter());
        vm.ShowOnlyMissing = true;

        Assert.DoesNotContain(found,   vm.VisibleRows);
        Assert.Contains(missing, vm.VisibleRows);
    }

    [Fact]
    public void ShowOnlyMissing_False_ShowsAllRows()
    {
        var found   = MakeRow(VoPresence.Found);
        var missing = MakeRow(VoPresence.Missing);
        var vm = new BatchVoImportViewModel([found, missing], new StubImporter());
        vm.ShowOnlyMissing = false;

        Assert.Contains(found,   vm.VisibleRows);
        Assert.Contains(missing, vm.VisibleRows);
    }

    // ── ImportCommand ────────────────────────────────────────────────────

    [Fact]
    public async Task Import_SetsRowStatusDone_WhenImporterSucceeds()
    {
        var row = MakeRow(primarySrc: "C:/src/a.wem");
        var stub = new StubImporter(success: true);
        var vm = new BatchVoImportViewModel([row], stub);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(BatchRowStatus.Done, row.RowStatus);
        Assert.Equal(1, stub.CallCount);
    }

    [Fact]
    public async Task Import_SetsRowStatusError_WhenImporterFails()
    {
        var row = MakeRow(primarySrc: "C:/src/a.wem");
        var stub = new StubImporter(success: false);
        var vm = new BatchVoImportViewModel([row], stub);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(BatchRowStatus.Error, row.RowStatus);
        Assert.NotNull(row.ErrorMessage);
    }

    [Fact]
    public async Task Import_SkipsRowsWithoutSourcePath()
    {
        var noSource = MakeRow();          // PrimarySourcePath = null
        var withSrc  = MakeRow(primarySrc: "C:/src/b.wem");
        var stub = new StubImporter(success: true);
        var vm = new BatchVoImportViewModel([noSource, withSrc], stub);

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(BatchRowStatus.Pending, noSource.RowStatus);
        Assert.Equal(BatchRowStatus.Done,    withSrc.RowStatus);
        Assert.Equal(1, stub.CallCount);
    }

    [Fact]
    public async Task Import_StopsOnCancellation_RemainingRowsStayPending()
    {
        var row1 = MakeRow(primarySrc: "C:/src/a.wem");
        var row2 = MakeRow(primarySrc: "C:/src/b.wem");
        BatchVoImportViewModel? captured = null;
        var stub = new StubImporter(onCall: () => captured?.Cancel());
        var vm = new BatchVoImportViewModel([row1, row2], stub);
        captured = vm;

        await vm.ImportCommand.ExecuteAsync(null);

        Assert.Equal(BatchRowStatus.Done,    row1.RowStatus);
        Assert.Equal(BatchRowStatus.Pending, row2.RowStatus);
    }

    // ── Stub ─────────────────────────────────────────────────────────────

    private sealed class StubImporter : IVoImporter
    {
        private readonly bool _success;
        private readonly Action? _onCall;
        public int CallCount { get; private set; }

        public StubImporter(bool success = true, Action? onCall = null)
        {
            _success = success;
            _onCall  = onCall;
        }

        public bool IsWwiseAvailable => false;

        public Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct)
        {
            CallCount++;
            _onCall?.Invoke();
            return Task.FromResult(new VoImportResult(_success,
                _success ? null : "Stub failure"));
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: compiler errors (types not found) or red tests — good.

- [ ] **Step 3: Write BatchVoImportViewModel.cs**

Create `DialogEditor.ViewModels/ViewModels/BatchVoImportViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public enum BatchRowStatus { Pending, Importing, Done, Error }

public partial class BatchVoRowViewModel : ObservableObject
{
    // ── Init-only ────────────────────────────────────────────────────────
    public string     ConversationName { get; }
    public int        NodeId           { get; }
    public string     TextPreview      { get; }
    public VoPresence VoStatus         { get; }
    public string     DestPrimaryPath  { get; }
    public string     DestFemPath      { get; }

    // ── Observable ───────────────────────────────────────────────────────
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPrimarySource))]
    [NotifyPropertyChangedFor(nameof(PrimaryFileLabel))]
    [NotifyPropertyChangedFor(nameof(PrimaryPlayGlyph))]
    private string? _primarySourcePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FemFileLabel))]
    [NotifyPropertyChangedFor(nameof(FemPlayGlyph))]
    private string? _femSourcePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RowStatusGlyph))]
    private BatchRowStatus _rowStatus = BatchRowStatus.Pending;

    [ObservableProperty] private string? _errorMessage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PrimaryPlayGlyph))]
    private bool _isPlayingPrimary;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FemPlayGlyph))]
    private bool _isPlayingFem;

    // ── Computed ─────────────────────────────────────────────────────────
    public bool   HasPrimarySource  => PrimarySourcePath is not null;
    public string PrimaryFileLabel  => PrimarySourcePath is not null
        ? Path.GetFileName(PrimarySourcePath) : "—";
    public string FemFileLabel      => FemSourcePath is not null
        ? Path.GetFileName(FemSourcePath) : "—";
    public string VoStatusGlyph     => VoStatus == VoPresence.Found ? "✓" : "✗";
    public string RowStatusGlyph    => RowStatus switch
    {
        BatchRowStatus.Done     => "✓",
        BatchRowStatus.Error    => "✗",
        BatchRowStatus.Importing => "…",
        _                       => ""
    };
    public string PrimaryPlayGlyph  => IsPlayingPrimary ? "■" : "▶";
    public string FemPlayGlyph      => IsPlayingFem     ? "■" : "▶";

    public BatchVoRowViewModel(
        string conversationName, int nodeId, string textPreview,
        VoPresence voStatus, string destPrimaryPath, string destFemPath)
    {
        ConversationName = conversationName;
        NodeId           = nodeId;
        TextPreview      = textPreview;
        VoStatus         = voStatus;
        DestPrimaryPath  = destPrimaryPath;
        DestFemPath      = destFemPath;
    }
}

public partial class BatchVoImportViewModel : ObservableObject
{
    private readonly IVoImporter _importer;
    private CancellationTokenSource _cts = new();

    public IReadOnlyList<BatchVoRowViewModel> AllRows    { get; }
    public ObservableCollection<BatchVoRowViewModel> VisibleRows { get; } = [];

    [ObservableProperty] private bool       _showOnlyMissing = true;
    [ObservableProperty] private WemQuality _quality         = WemQuality.Medium;
    [ObservableProperty] private bool       _isImporting;
    [ObservableProperty] private string     _progressText    = string.Empty;

    public BatchVoImportViewModel(IReadOnlyList<BatchVoRowViewModel> rows, IVoImporter importer)
    {
        AllRows   = rows;
        _importer = importer;
        RefreshVisibleRows();
    }

    partial void OnShowOnlyMissingChanged(bool value) => RefreshVisibleRows();

    private void RefreshVisibleRows()
    {
        VisibleRows.Clear();
        foreach (var row in AllRows)
            if (!ShowOnlyMissing || row.VoStatus != VoPresence.Found)
                VisibleRows.Add(row);
    }

    // Called from dialog code-behind after Browse/Clear so CanExecute re-evaluates.
    public void OnRowChanged() => ImportCommand.NotifyCanExecuteChanged();

    public void Cancel()
    {
        if (IsImporting) _cts.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        _cts = new CancellationTokenSource();
        IsImporting = true;
        ImportCommand.NotifyCanExecuteChanged();

        var toImport = AllRows.Where(r => r.HasPrimarySource).ToList();
        var done     = 0;
        ProgressText = Loc.Format("BatchVoImport_Progress", done, toImport.Count);

        try
        {
            foreach (var row in toImport)
            {
                _cts.Token.ThrowIfCancellationRequested();
                row.RowStatus = BatchRowStatus.Importing;
                try
                {
                    var result = await _importer.ImportAsync(
                        new VoImportRequest(
                            row.DestPrimaryPath, row.PrimarySourcePath!,
                            row.DestFemPath,     row.FemSourcePath,
                            Quality),
                        _cts.Token);

                    if (result.Success)
                        row.RowStatus = BatchRowStatus.Done;
                    else
                    {
                        row.RowStatus    = BatchRowStatus.Error;
                        row.ErrorMessage = result.ErrorMessage;
                        AppLog.Error($"Batch VO import failed for node {row.NodeId}: {result.ErrorMessage}");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    AppLog.Error($"Batch VO import exception for node {row.NodeId}", ex);
                    row.RowStatus    = BatchRowStatus.Error;
                    row.ErrorMessage = ex.Message;
                }

                done++;
                ProgressText = Loc.Format("BatchVoImport_Progress", done, toImport.Count);
            }
        }
        catch (OperationCanceledException) { /* deliberate cancellation */ }
        finally
        {
            IsImporting = false;
            ImportCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanImport() => !IsImporting && AllRows.Any(r => r.HasPrimarySource);
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: all 5 new tests pass.

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/ViewModels/BatchVoImportViewModel.cs DialogEditor.Tests/ViewModels/BatchVoImportViewModelTests.cs
git commit -m "feat(batch-vo): add BatchVoRowViewModel + BatchVoImportViewModel with tests"
```

---

### Task 2: ConversationViewModel additions + NodeDetailViewModel.Refresh() + MainWindowViewModel sync

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` (lines 438–502)
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs` (top of file + BuildSnapshot region)
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (3 sites: lines ~426, ~467, ~592)
- Test: `DialogEditor.Tests/ViewModels/ConversationViewModelBatchVoTests.cs`

**Interfaces:**
- Consumes: `VoPathResolver.Check`, `ChatterPrefixService`, `BatchVoRowViewModel` (Task 1)
- Produces:
  - `NodeDetailViewModel.Refresh()` — public wrapper calling `NotifyAllProxies()`
  - `ConversationViewModel.ProjectPath { get; set; }` — set by MainWindowViewModel
  - `ConversationViewModel.ShowBatchVoImport` — `Func<Task>?`, set by MainWindow
  - `ConversationViewModel.BatchImportVoCommand` — generated by `[RelayCommand(CanExecute = nameof(CanBatchImportVo))]`
  - `ConversationViewModel.CanBatchImportVo` — computed bool
  - `ConversationViewModel.BuildBatchVoRows(string gameRoot, string activeGameId)` — `IReadOnlyList<BatchVoRowViewModel>`

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/ConversationViewModelBatchVoTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class ConversationViewModelBatchVoTests : IDisposable
{
    private readonly string _gameRoot;
    private readonly string _voRoot;
    private const string KnownGuid   = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";
    private const string KnownPrefix = "eder";

    public ConversationViewModelBatchVoTests()
    {
        Loc.Configure(new StubStringProvider());
        _gameRoot = Path.Combine(Path.GetTempPath(), $"BatchVoVm_{Guid.NewGuid():N}");
        _voRoot   = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        Directory.CreateDirectory(_voRoot);
        ChatterPrefixService.Register(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                { { KnownGuid, KnownPrefix } });
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        if (Directory.Exists(_gameRoot))
            Directory.Delete(_gameRoot, recursive: true);
    }

    private static ConversationNode MakeVoNode(int id, string guid = KnownGuid) =>
        new(id, false, SpeakerCategory.Npc, guid, "",
            [], [], [], "", "", HasVO: true);

    private static ConversationNode MakeNoVoNode(int id) =>
        new(id, false, SpeakerCategory.Npc, KnownGuid, "",
            [], [], [], "", "");   // HasVO defaults to false

    private static ConversationViewModel LoadVm(params ConversationNode[] nodes)
    {
        var vm   = new ConversationViewModel(new StubDispatcher());
        var conv = new Conversation("test_conv", nodes, StringTable.Empty);
        vm.Load(conv);
        vm.ProjectPath = "C:/project/test.json";
        return vm;
    }

    [Fact]
    public void BuildBatchVoRows_ExcludesNotApplicableNodes()
    {
        var vm = LoadVm(MakeVoNode(1), MakeNoVoNode(2));

        var rows = vm.BuildBatchVoRows(_gameRoot, "poe2");

        Assert.Single(rows);
        Assert.Equal(1, rows[0].NodeId);
    }

    [Fact]
    public void BuildBatchVoRows_SortsByNodeId()
    {
        var vm = LoadVm(MakeVoNode(5), MakeVoNode(2), MakeVoNode(8));

        var rows = vm.BuildBatchVoRows(_gameRoot, "poe2");

        Assert.Equal([2, 5, 8], rows.Select(r => r.NodeId).ToArray());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: `BuildBatchVoRows` method not found — red.

- [ ] **Step 3: Add NodeDetailViewModel.Refresh()**

In `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`, add after the `NotifyAllProxies()` closing brace (line ~502):

```csharp
/// Refreshes all computed properties (including VO-file status) after an external operation.
public void Refresh() => NotifyAllProxies();
```

- [ ] **Step 4: Add ConversationViewModel changes**

In `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`:

**4a.** After the `public NodeDetailViewModel? Detail { get; set; }` line (~line 22), add:

```csharp
/// Set by MainWindowViewModel on project create/open/close — mirrors Detail.ProjectPath.
private string? _projectPath;
public string? ProjectPath
{
    get => _projectPath;
    set
    {
        _projectPath = value;
        BatchImportVoCommand.NotifyCanExecuteChanged();
    }
}

/// Wired by MainWindow.axaml.cs — opens the batch import dialog.
public Func<Task>? ShowBatchVoImport { get; set; }

public bool CanBatchImportVo =>
    ProjectPath is not null &&
    ShowBatchVoImport is not null &&
    Nodes.Any(n => n.HasVO || !string.IsNullOrEmpty(n.ExternalVO));

[RelayCommand(CanExecute = nameof(CanBatchImportVo))]
private async Task BatchImportVo()
{
    if (ShowBatchVoImport is not null)
        await ShowBatchVoImport();
}
```

**4b.** In the `Nodes.CollectionChanged` handler constructor block (line ~35), append `BatchImportVoCommand.NotifyCanExecuteChanged();` after the `RefreshStatistics();` call:

```csharp
Nodes.CollectionChanged += (_, args) =>
{
    // ... existing code ...
    RefreshStatistics();
    BatchImportVoCommand.NotifyCanExecuteChanged();  // ← add this line
};
```

**4c.** Add `BuildBatchVoRows` before the closing `}` of the class (after `BuildSnapshot`, line ~626):

```csharp
/// Returns one BatchVoRowViewModel per VO node in the current conversation,
/// sorted by NodeId. Skips nodes where VoPathResolver returns null or NotApplicable.
/// Returns empty list if ProjectPath is null (project not saved).
public IReadOnlyList<BatchVoRowViewModel> BuildBatchVoRows(string gameRoot, string activeGameId)
{
    if (ProjectPath is null) return [];

    var voRoot = Path.Combine(gameRoot,
        "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
    var voDir = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo");
    var rows  = new List<BatchVoRowViewModel>();

    foreach (var node in Nodes)
    {
        var check = VoPathResolver.Check(
            node.SpeakerGuid, node.HasVO, node.ExternalVO,
            node.NodeId, ConversationName, gameRoot, activeGameId);

        if (check is null || check.Status == VoPresence.NotApplicable) continue;
        if (check.PrimaryWemPath is null) continue;

        var rel         = Path.GetRelativePath(voRoot, check.PrimaryWemPath);
        var destPrimary = Path.Combine(voDir, rel);
        var destFem     = Path.Combine(voDir, rel[..^4] + "_fem.wem");

        var raw     = node.DefaultText.Trim();
        var preview = raw.Length == 0 ? $"Node {node.NodeId}"
                    : raw.Length <= 60 ? raw
                    : raw[..60] + "…";

        rows.Add(new BatchVoRowViewModel(
            ConversationName, node.NodeId, preview,
            check.Status, destPrimary, destFem));
    }

    return rows.OrderBy(r => r.NodeId).ToList();
}
```

The method also needs `using System.Linq;` and `using DialogEditor.ViewModels.Services;` — verify they're already in the file's using block (they should be). Add `using DialogEditor.ViewModels;` is already the namespace, so no extra using needed for `BatchVoRowViewModel`.

- [ ] **Step 5: Sync Canvas.ProjectPath in MainWindowViewModel**

In `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`, at each of the 3 sites where `Detail.ProjectPath` is assigned, add the matching `Canvas.ProjectPath` assignment on the next line:

Site 1 (~line 426) — new project:
```csharp
_projectPath = path;
Detail.ProjectPath = _projectPath;
Canvas.ProjectPath = _projectPath;   // ← add
```

Site 2 (~line 467) — load failure (set to null):
```csharp
_projectPath = null;
Detail.ProjectPath = null;
Canvas.ProjectPath = null;           // ← add
```

Site 3 (~line 592) — open project:
```csharp
_projectPath = path;
Detail.ProjectPath = _projectPath;
Canvas.ProjectPath = _projectPath;   // ← add
```

- [ ] **Step 6: Run tests to verify they pass**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: the 2 new `ConversationViewModelBatchVoTests` pass; all existing tests still pass.

- [ ] **Step 7: Commit**

```
git add DialogEditor.ViewModels/ViewModels/BatchVoImportViewModel.cs
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs
git add DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git add DialogEditor.Tests/ViewModels/ConversationViewModelBatchVoTests.cs
git commit -m "feat(batch-vo): add BatchImportVoCommand + BuildBatchVoRows to ConversationViewModel"
```

---

### Task 3: Strings + BatchVoImportDialog

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Create: `DialogEditor.Avalonia/Views/BatchVoImportDialog.axaml`
- Create: `DialogEditor.Avalonia/Views/BatchVoImportDialog.axaml.cs`

**Interfaces:**
- Consumes: `BatchVoImportViewModel` (Task 1), `BatchVoRowViewModel` (Task 1), `IVoAudioPlayer` (existing), `Loc.Get` (existing), `PickVoFileAsync` pattern from `VoImportDialog.axaml.cs`
- Produces: `BatchVoImportDialog(BatchVoImportViewModel vm, IVoAudioPlayer player, bool isSingleConversation)`

No automated tests for this task (consistent with existing dialog precedent). Verify with a build.

- [ ] **Step 1: Add string resources**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, after the last `ToolTip_VoQuality` entry, add:

```xml
<!-- Batch VO Import dialog -->
<x:String x:Key="BatchVoImport_Title_Single">Batch Import Voice-Over</x:String>
<x:String x:Key="BatchVoImport_Title_All">Batch Import Voice-Over — All Conversations</x:String>
<x:String x:Key="BatchVoImport_ShowOnlyMissing">Show only missing</x:String>
<x:String x:Key="BatchVoImport_ImportButton">Import</x:String>
<x:String x:Key="BatchVoImport_NodeColumn">Node</x:String>
<x:String x:Key="BatchVoImport_ConversationColumn">Conversation</x:String>
<x:String x:Key="BatchVoImport_TextColumn">Text</x:String>
<x:String x:Key="BatchVoImport_StatusColumn">Status</x:String>
<x:String x:Key="BatchVoImport_PrimaryColumn">Primary</x:String>
<x:String x:Key="BatchVoImport_FemColumn">Female</x:String>
<x:String x:Key="BatchVoImport_RowStatusColumn">Result</x:String>
<x:String x:Key="BatchVoImport_RowCount">{0} nodes</x:String>
<x:String x:Key="BatchVoImport_Progress">{0} / {1} imported</x:String>
<x:String x:Key="BatchVoImport_QualityLabel">Quality (WAV only)</x:String>
<x:String x:Key="ToolTip_BatchVoImport">Open a table of all VO nodes in this conversation and browse for source files to import.</x:String>
<x:String x:Key="ToolTip_BatchVoImportAll">Open a table of all VO nodes across every conversation in this project and browse for source files to import.</x:String>
<x:String x:Key="ToolTip_BatchBrowsePrimary">Browse for a .wem or .wav file for this node's primary voice-over slot.</x:String>
<x:String x:Key="ToolTip_BatchBrowseFem">Browse for a .wem or .wav file for this node's female voice-over slot.</x:String>
<x:String x:Key="ToolTip_BatchClearPrimary">Clear the selected primary voice-over file for this row.</x:String>
<x:String x:Key="ToolTip_BatchClearFem">Clear the selected female voice-over file for this row.</x:String>
<x:String x:Key="ToolTip_BatchPlayPrimary">Play the selected primary voice-over file for this row.</x:String>
<x:String x:Key="ToolTip_BatchPlayFem">Play the selected female voice-over file for this row.</x:String>
<x:String x:Key="ToolTip_BatchShowOnlyMissing">When checked, hides rows whose VO file already exists in the game folder.</x:String>
<x:String x:Key="Menu_BatchImportVo">Batch Import Voice-Over…</x:String>
<x:String x:Key="Menu_BatchImportVoAll">Batch Import Voice-Over… (All Conversations)</x:String>
<x:String x:Key="ToolTip_Menu_BatchImportVo">Import voice-over files for multiple nodes in the current conversation at once.</x:String>
<x:String x:Key="ToolTip_Menu_BatchImportVoAll">Import voice-over files for multiple nodes across all conversations in this project at once.</x:String>
```

- [ ] **Step 2: Create BatchVoImportDialog.axaml**

Create `DialogEditor.Avalonia/Views/BatchVoImportDialog.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        x:Class="DialogEditor.Avalonia.Views.BatchVoImportDialog"
        Title="{DynamicResource BatchVoImport_Title_Single}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="900" Height="560"
        MinWidth="700" MinHeight="300"
        WindowStartupLocation="CenterOwner"
        x:DataType="vm:BatchVoImportViewModel">

    <DockPanel>

        <!-- Header: ShowOnlyMissing + row count + quality -->
        <Grid DockPanel.Dock="Top"
              ColumnDefinitions="Auto,*,Auto"
              Margin="12,8,12,4">
            <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="16">
                <CheckBox Content="{DynamicResource BatchVoImport_ShowOnlyMissing}"
                          IsChecked="{Binding ShowOnlyMissing}"
                          ToolTip.Tip="{DynamicResource ToolTip_BatchShowOnlyMissing}"
                          AutomationProperties.HelpText="{DynamicResource ToolTip_BatchShowOnlyMissing}"/>
            </StackPanel>

            <!-- Quality -->
            <StackPanel Grid.Column="2" Orientation="Horizontal" Spacing="12"
                        x:Name="QualityPanel" IsEnabled="{Binding !IsImporting}">
                <TextBlock Text="{DynamicResource BatchVoImport_QualityLabel}"
                           VerticalAlignment="Center"/>
                <RadioButton x:Name="BatchQualityLow"
                             Content="{DynamicResource VoImport_Quality_Low}"
                             GroupName="BatchWemQuality"
                             Checked="BatchQualityLow_Checked"
                             ToolTip.Tip="{DynamicResource ToolTip_VoQuality}"
                             AutomationProperties.HelpText="{DynamicResource ToolTip_VoQuality}"/>
                <RadioButton x:Name="BatchQualityMedium"
                             Content="{DynamicResource VoImport_Quality_Medium}"
                             GroupName="BatchWemQuality"
                             IsChecked="True"
                             Checked="BatchQualityMedium_Checked"
                             ToolTip.Tip="{DynamicResource ToolTip_VoQuality}"
                             AutomationProperties.HelpText="{DynamicResource ToolTip_VoQuality}"/>
                <RadioButton x:Name="BatchQualityHigh"
                             Content="{DynamicResource VoImport_Quality_High}"
                             GroupName="BatchWemQuality"
                             Checked="BatchQualityHigh_Checked"
                             ToolTip.Tip="{DynamicResource ToolTip_VoQuality}"
                             AutomationProperties.HelpText="{DynamicResource ToolTip_VoQuality}"/>
            </StackPanel>
        </Grid>

        <!-- Footer: progress + Import + Cancel -->
        <Grid DockPanel.Dock="Bottom"
              ColumnDefinitions="*,Auto,Auto"
              Margin="12,4,12,8">
            <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="8"
                        IsVisible="{Binding IsImporting}" VerticalAlignment="Center">
                <ProgressBar IsIndeterminate="True" Width="120"/>
                <TextBlock Text="{Binding ProgressText}" VerticalAlignment="Center"/>
            </StackPanel>
            <Button Grid.Column="1" x:Name="ImportButton"
                    Content="{DynamicResource BatchVoImport_ImportButton}"
                    Command="{Binding ImportCommand}"
                    ToolTip.Tip="{DynamicResource ToolTip_BatchVoImport}"
                    AutomationProperties.HelpText="{DynamicResource ToolTip_BatchVoImport}"
                    Margin="0,0,8,0"/>
            <Button Grid.Column="2" x:Name="CancelButton"
                    Content="Cancel"
                    Click="Cancel_Click"/>
        </Grid>

        <!-- DataGrid: fills remaining height -->
        <DataGrid x:Name="RowsGrid"
                  ItemsSource="{Binding VisibleRows}"
                  IsReadOnly="True"
                  CanUserSortColumns="True"
                  CanUserResizeColumns="True"
                  GridLinesVisibility="Horizontal"
                  IsEnabled="{Binding !IsImporting}">
            <DataGrid.Columns>

                <!-- Conversation: hidden for single-conversation mode (set in code-behind) -->
                <DataGridTextColumn x:Name="ConversationColumn"
                                    Header="{DynamicResource BatchVoImport_ConversationColumn}"
                                    Binding="{Binding ConversationName}"
                                    Width="160"/>

                <DataGridTextColumn Header="{DynamicResource BatchVoImport_NodeColumn}"
                                    Binding="{Binding NodeId}"
                                    Width="60"/>

                <DataGridTextColumn Header="{DynamicResource BatchVoImport_TextColumn}"
                                    Binding="{Binding TextPreview}"
                                    Width="*"/>

                <DataGridTextColumn Header="{DynamicResource BatchVoImport_StatusColumn}"
                                    Binding="{Binding VoStatusGlyph}"
                                    Width="60"/>

                <!-- Primary source slot -->
                <DataGridTemplateColumn Header="{DynamicResource BatchVoImport_PrimaryColumn}"
                                        Width="240">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate DataType="{x:Type vm:BatchVoRowViewModel}">
                            <Grid ColumnDefinitions="*,Auto,Auto,Auto" Margin="4,0">
                                <TextBlock Grid.Column="0"
                                           Text="{Binding PrimaryFileLabel}"
                                           VerticalAlignment="Center"
                                           TextTrimming="CharacterEllipsis"/>
                                <Button Grid.Column="1"
                                        Content="{Binding PrimaryPlayGlyph}"
                                        IsVisible="{Binding HasPrimarySource}"
                                        Click="PlayPrimary_Click"
                                        ToolTip.Tip="{DynamicResource ToolTip_BatchPlayPrimary}"
                                        AutomationProperties.HelpText="{DynamicResource ToolTip_BatchPlayPrimary}"
                                        Padding="4,1" Margin="2,0"/>
                                <Button Grid.Column="2"
                                        Content="{DynamicResource VoImport_Browse}"
                                        Click="BrowsePrimary_Click"
                                        ToolTip.Tip="{DynamicResource ToolTip_BatchBrowsePrimary}"
                                        AutomationProperties.HelpText="{DynamicResource ToolTip_BatchBrowsePrimary}"
                                        Margin="2,0"/>
                                <Button Grid.Column="3"
                                        Content="{DynamicResource VoImport_Clear}"
                                        IsVisible="{Binding HasPrimarySource}"
                                        Click="ClearPrimary_Click"
                                        ToolTip.Tip="{DynamicResource ToolTip_BatchClearPrimary}"
                                        AutomationProperties.HelpText="{DynamicResource ToolTip_BatchClearPrimary}"/>
                            </Grid>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>

                <!-- Female source slot -->
                <DataGridTemplateColumn Header="{DynamicResource BatchVoImport_FemColumn}"
                                        Width="240">
                    <DataGridTemplateColumn.CellTemplate>
                        <DataTemplate DataType="{x:Type vm:BatchVoRowViewModel}">
                            <Grid ColumnDefinitions="*,Auto,Auto,Auto" Margin="4,0">
                                <TextBlock Grid.Column="0"
                                           Text="{Binding FemFileLabel}"
                                           VerticalAlignment="Center"
                                           TextTrimming="CharacterEllipsis"/>
                                <Button Grid.Column="1"
                                        Content="{Binding FemPlayGlyph}"
                                        IsVisible="{Binding FemSourcePath, Converter={x:Static ObjectConverters.IsNotNull}}"
                                        Click="PlayFem_Click"
                                        ToolTip.Tip="{DynamicResource ToolTip_BatchPlayFem}"
                                        AutomationProperties.HelpText="{DynamicResource ToolTip_BatchPlayFem}"
                                        Padding="4,1" Margin="2,0"/>
                                <Button Grid.Column="2"
                                        Content="{DynamicResource VoImport_Browse}"
                                        Click="BrowseFem_Click"
                                        ToolTip.Tip="{DynamicResource ToolTip_BatchBrowseFem}"
                                        AutomationProperties.HelpText="{DynamicResource ToolTip_BatchBrowseFem}"
                                        Margin="2,0"/>
                                <Button Grid.Column="3"
                                        Content="{DynamicResource VoImport_Clear}"
                                        IsVisible="{Binding FemSourcePath, Converter={x:Static ObjectConverters.IsNotNull}}"
                                        Click="ClearFem_Click"
                                        ToolTip.Tip="{DynamicResource ToolTip_BatchClearFem}"
                                        AutomationProperties.HelpText="{DynamicResource ToolTip_BatchClearFem}"/>
                            </Grid>
                        </DataTemplate>
                    </DataGridTemplateColumn.CellTemplate>
                </DataGridTemplateColumn>

                <!-- Row import status -->
                <DataGridTextColumn Header="{DynamicResource BatchVoImport_RowStatusColumn}"
                                    Binding="{Binding RowStatusGlyph}"
                                    Width="60"/>

            </DataGrid.Columns>
        </DataGrid>

    </DockPanel>
</Window>
```

Note: `x:Name="ConversationColumn"` — Avalonia 11's AXAML compiler generates a typed field for named `DataGridColumn` instances. If the build rejects this, fall back to accessing by index in code-behind: `RowsGrid.Columns[0].IsVisible = !isSingleConversation`.

- [ ] **Step 3: Create BatchVoImportDialog.axaml.cs**

Create `DialogEditor.Avalonia/Views/BatchVoImportDialog.axaml.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class BatchVoImportDialog : Window
{
    private readonly BatchVoImportViewModel _vm   = null!;
    private readonly IVoAudioPlayer         _player = null!;

    private BatchVoRowViewModel? _playingRow;
    private bool                 _playingPrimary;

    // Parameterless ctor required to avoid AVLN3001.
    public BatchVoImportDialog() => InitializeComponent();

    public BatchVoImportDialog(
        BatchVoImportViewModel vm, IVoAudioPlayer player, bool isSingleConversation)
    {
        InitializeComponent();
        DataContext = _vm   = vm;
        _player              = player;

        _player.PlaybackStopped += OnPlaybackStopped;
        Closed += (_, _) =>
        {
            _player.PlaybackStopped -= OnPlaybackStopped;
            _player.Stop();
        };

        Title = Loc.Get(isSingleConversation
            ? "BatchVoImport_Title_Single"
            : "BatchVoImport_Title_All");

        // Hide Conversation column in single-conversation mode.
        // DataGridColumn named fields are generated by Avalonia 11 AXAML compiler.
        // Fallback: if ConversationColumn is not generated, use RowsGrid.Columns[0].IsVisible.
        ConversationColumn.IsVisible = !isSingleConversation;
    }

    // ── Quality ──────────────────────────────────────────────────────────

    private void BatchQualityLow_Checked(object?    s, RoutedEventArgs e) => _vm.Quality = WemQuality.Low;
    private void BatchQualityMedium_Checked(object? s, RoutedEventArgs e) => _vm.Quality = WemQuality.Medium;
    private void BatchQualityHigh_Checked(object?   s, RoutedEventArgs e) => _vm.Quality = WemQuality.High;

    // ── Browse / Clear — Primary ─────────────────────────────────────────

    private async void BrowsePrimary_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var path = await PickVoFileAsync();
        if (path is null) return;
        row.PrimarySourcePath = path;
        _vm.OnRowChanged();
    }

    private void ClearPrimary_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        if (_playingRow == row && _playingPrimary) StopPlayback();
        row.PrimarySourcePath = null;
        _vm.OnRowChanged();
    }

    // ── Browse / Clear — Female ──────────────────────────────────────────

    private async void BrowseFem_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        var path = await PickVoFileAsync();
        if (path is null) return;
        row.FemSourcePath = path;
    }

    private void ClearFem_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        if (_playingRow == row && !_playingPrimary) StopPlayback();
        row.FemSourcePath = null;
    }

    // ── Play — Primary / Female ──────────────────────────────────────────

    private void PlayPrimary_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row || row.PrimarySourcePath is null) return;
        if (_playingRow == row && _playingPrimary) { StopPlayback(); return; }
        StartPlayback(row, primary: true);
    }

    private void PlayFem_Click(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row || row.FemSourcePath is null) return;
        if (_playingRow == row && !_playingPrimary) { StopPlayback(); return; }
        StartPlayback(row, primary: false);
    }

    private void StartPlayback(BatchVoRowViewModel row, bool primary)
    {
        StopPlayback();
        _playingRow     = row;
        _playingPrimary = primary;
        var path = primary ? row.PrimarySourcePath! : row.FemSourcePath!;
        _player.Play(path);
        if (primary) row.IsPlayingPrimary = true;
        else         row.IsPlayingFem     = true;
    }

    private void StopPlayback()
    {
        _player.Stop();
        if (_playingRow is not null)
        {
            if (_playingPrimary) _playingRow.IsPlayingPrimary = false;
            else                  _playingRow.IsPlayingFem    = false;
            _playingRow = null;
        }
    }

    private void OnPlaybackStopped()
    {
        if (_playingRow is not null)
        {
            if (_playingPrimary) _playingRow.IsPlayingPrimary = false;
            else                  _playingRow.IsPlayingFem    = false;
            _playingRow = null;
        }
    }

    // ── Cancel / Close ───────────────────────────────────────────────────

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.IsImporting) _vm.Cancel();
        else                  Close();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static BatchVoRowViewModel? RowOf(object? sender) =>
        (sender as Control)?.DataContext as BatchVoRowViewModel;

    private async Task<string?> PickVoFileAsync()
    {
        var options = new FilePickerOpenOptions
        {
            Title          = Loc.Get("VoImport_PickerTitle"),
            AllowMultiple  = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("VoImport_FileType_All"))
                    { Patterns = ["*.wem", "*.wav"] },
                new FilePickerFileType(Loc.Get("VoImport_FileType_Wem"))
                    { Patterns = ["*.wem"] },
                new FilePickerFileType(Loc.Get("VoImport_FileType_Wav"))
                    { Patterns = ["*.wav"] },
            ],
        };
        var files = await StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }
}
```

- [ ] **Step 4: Build to verify**

```
dotnet build DialogEditor.Avalonia
```

Expected: build succeeds (or only LSP-stale warnings, not errors). Fix any genuine errors before continuing.

If `ConversationColumn` is not found as a generated field, replace the `ConversationColumn.IsVisible = ...` line in the constructor with:

```csharp
if (isSingleConversation && RowsGrid.Columns.Count > 0)
    RowsGrid.Columns[0].IsVisible = false;
```

- [ ] **Step 5: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml
git add DialogEditor.Avalonia/Views/BatchVoImportDialog.axaml
git add DialogEditor.Avalonia/Views/BatchVoImportDialog.axaml.cs
git commit -m "feat(batch-vo): add BatchVoImportDialog with DataGrid, play, browse, quality"
```

---

### Task 4: Entry Points — Canvas Context Menu + MainWindow Wire-Up

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml` (canvas ContextMenu, lines 116–129)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (wire-up section, lines ~103–115)

**Interfaces:**
- Consumes: `ConversationViewModel.BatchImportVoCommand`, `ConversationViewModel.CanBatchImportVo`, `ConversationViewModel.ShowBatchVoImport`, `ConversationViewModel.BuildBatchVoRows`, `NodeDetailViewModel.Refresh()`, `BatchVoImportViewModel` (Task 1), `BatchVoImportDialog` (Task 3)
- Produces: working end-to-end "Batch Import Voice-Over…" flow from canvas right-click

- [ ] **Step 1: Add canvas context menu item**

In `DialogEditor.Avalonia/Views/ConversationView.axaml`, inside `<nodify:NodifyEditor.ContextMenu>` (lines 116–129), add after the existing `Menu_Canvas_AddAnnotationHere` item:

```xml
<Separator IsVisible="{Binding CanBatchImportVo}"/>
<MenuItem Header="{DynamicResource Menu_BatchImportVo}"
          Command="{Binding BatchImportVoCommand}"
          IsVisible="{Binding CanBatchImportVo}"
          ToolTip.Tip="{DynamicResource ToolTip_Menu_BatchImportVo}"
          AutomationProperties.HelpText="{DynamicResource ToolTip_Menu_BatchImportVo}"/>
```

The result should look like:

```xml
<nodify:NodifyEditor.ContextMenu>
    <ContextMenu>
        <MenuItem Header="{DynamicResource Menu_Canvas_AddNodeHere}"
                  Click="CanvasMenu_AddNode_Click"
                  IsVisible="{Binding IsEditable}"
                  ToolTip.Tip="{DynamicResource ToolTip_Menu_Canvas_AddNodeHere}"
                  AutomationProperties.HelpText="{DynamicResource ToolTip_Menu_Canvas_AddNodeHere}"/>
        <MenuItem Header="{DynamicResource Menu_Canvas_AddAnnotationHere}"
                  Click="CanvasMenu_AddAnnotation_Click"
                  IsVisible="{Binding IsEditable}"
                  ToolTip.Tip="{DynamicResource ToolTip_Menu_Canvas_AddAnnotationHere}"
                  AutomationProperties.HelpText="{DynamicResource ToolTip_Menu_Canvas_AddAnnotationHere}"/>
        <Separator IsVisible="{Binding CanBatchImportVo}"/>
        <MenuItem Header="{DynamicResource Menu_BatchImportVo}"
                  Command="{Binding BatchImportVoCommand}"
                  IsVisible="{Binding CanBatchImportVo}"
                  ToolTip.Tip="{DynamicResource ToolTip_Menu_BatchImportVo}"
                  AutomationProperties.HelpText="{DynamicResource ToolTip_Menu_BatchImportVo}"/>
    </ContextMenu>
</nodify:NodifyEditor.ContextMenu>
```

- [ ] **Step 2: Wire ShowBatchVoImport in MainWindow.axaml.cs**

In `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`, in the initialization block where `vm.Detail.ShowImportDialog` is set (around lines 103–115), add the `ShowBatchVoImport` delegate immediately after:

```csharp
vm.Detail.ShowImportDialog = async paths =>
{
    var dialog = new VoImportDialog(voImporter, paths, audioPlayer);
    await dialog.ShowDialog(this);
    return dialog.Result;
};

// ← Add below:
vm.Canvas.ShowBatchVoImport = async () =>
{
    var rows = vm.Canvas.BuildBatchVoRows(vm.Detail.GameRoot, vm.Detail.ActiveGameId);
    if (rows.Count == 0) return;
    var batchVm = new BatchVoImportViewModel(rows, voImporter);
    var dlg     = new BatchVoImportDialog(batchVm, audioPlayer, isSingleConversation: true);
    await dlg.ShowDialog(this);
    vm.Detail.Refresh();
};
```

`vm.Detail.GameRoot` and `vm.Detail.ActiveGameId` are public properties on `NodeDetailViewModel` that hold the currently-loaded game folder. They default to `string.Empty` when no game is open, which causes `BuildBatchVoRows` to return an empty list (VoPathResolver returns null for non-poe2 game IDs), so the early-return guard (`if (rows.Count == 0) return;`) keeps the dialog from appearing in that case.

- [ ] **Step 3: Build both projects**

```
dotnet build DialogEditor.ViewModels
dotnet build DialogEditor.Avalonia
```

Expected: both succeed with zero errors.

- [ ] **Step 4: Run full test suite**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: all tests pass (including the 7 new tests added in Tasks 1 and 2).

- [ ] **Step 5: Commit**

```
git add DialogEditor.Avalonia/Views/ConversationView.axaml
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat(batch-vo): wire canvas context menu + MainWindow delegate for batch VO import"
```

---

## Self-Review Against Spec

**Coverage check:**

| Spec requirement | Covered |
|---|---|
| `BatchVoRowViewModel` with all listed properties | ✓ Task 1 |
| `BatchVoImportViewModel` with ShowOnlyMissing, Quality, ImportCommand, Cancel | ✓ Task 1 |
| `VisibleRows` rebuilt as `ObservableCollection` (not ICollectionView) | ✓ Task 1 |
| Import loop: per-row catch+log, OCE swallowed, continue | ✓ Task 1 |
| `ConversationViewModel.BuildBatchVoRows` | ✓ Task 2 |
| `ShowBatchVoImport` delegate + `BatchImportVoCommand` | ✓ Task 2 |
| `CanBatchImportVo` = ProjectPath + delegate + has VO nodes | ✓ Task 2 |
| All `BatchVoImport_*` string keys | ✓ Task 3 |
| Dialog: DataGrid, play buttons, quality row, progress, Import/Cancel | ✓ Task 3 |
| Canvas context menu entry point | ✓ Task 4 |
| MainWindow delegate wiring | ✓ Task 4 |
| Post-import `Detail.Refresh()` call | ✓ Task 4 |
| All 5 ViewModel tests | ✓ Task 1 |
| `BuildBatchVoRows_ExcludesNotApplicableNodes` + `SortsByNodeId` | ✓ Task 2 |

**Placeholder scan:** No TBDs or incomplete steps found.

**Type consistency:**
- `BatchVoRowViewModel.HasPrimarySource` used in `CanImport()` ✓ (consistent with property name)
- `BatchImportVoCommand` generated from `BatchImportVo` method ✓ (CommunityToolkit naming)
- `vm.Detail.Refresh()` relies on Task 2's `public void Refresh()` ✓

**Open item — Plan 2:** The `isSingleConversation: false` path and "All Conversations" menu item are deferred to Plan 2. The dialog already supports it via the constructor parameter and column visibility toggle.
