# Diff Before/After Text Detail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a writer selects a node on the read-only diff canvas, show that node's before/after Default and Female text in a panel below the canvas, with the changed portion highlighted inline.

**Architecture:** A new pure ViewModel (`NodeDiffDetailViewModel`) holds the presentation logic (placeholder substitution, female-row visibility, structural-only detection) and is unit-tested in isolation. `DiffViewModel` caches per-node before/after text while building the canvas, observes `DiffCanvas.SelectedNode`, and exposes `SelectedNodeDetail`. `DiffWindow` renders the inline word-level highlighting by reusing the existing `TextDiff` utility — the same pattern already used by `GitConflictResolutionWindow`.

**Tech Stack:** C# 12 / .NET 8, CommunityToolkit.Mvvm 8.2.2, Avalonia 11.3.14, xUnit 2.5.3, `Avalonia.Headless.XUnit` for UI tests. Reuses `DialogEditor.Patch.GitConflict.TextDiff`.

**Spec:** `docs/superpowers/specs/2026-05-31-diff-before-after-detail-design.md`

---

## File Structure

- **Create** `DialogEditor.ViewModels/ViewModels/NodeDiffDetailViewModel.cs` — pure before/after presentation model.
- **Create** `DialogEditor.Tests/ViewModels/NodeDiffDetailViewModelTests.cs` — unit tests for the model.
- **Modify** `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` — text-map caching, selection observation, `SelectedNodeDetail`.
- **Modify** `DialogEditor.Tests/ViewModels/DiffViewModelTests.cs` — integration tests for selection → detail.
- **Modify** `DialogEditor.Avalonia/Resources/Strings.axaml` — new localized keys.
- **Modify** `DialogEditor.Avalonia/Views/DiffWindow.axaml` — the detail panel.
- **Modify** `DialogEditor.Avalonia/Views/DiffWindow.axaml.cs` — inline rendering via `TextDiff`.
- **Modify** `DialogEditor.Tests/Views/DiffWindowTests.cs` — headless visibility tests.
- **Modify** `Gaps.md` — mark the before/after detail item implemented.

---

### Task 1: `NodeDiffDetailViewModel` and tests

**Files:**
- Create: `DialogEditor.Tests/ViewModels/NodeDiffDetailViewModelTests.cs`
- Create: `DialogEditor.ViewModels/ViewModels/NodeDiffDetailViewModel.cs`

This is pure logic with no dependencies on the canvas or game data, so it is fully unit-testable. `Loc.Configure(new StubStringProvider())` makes `Loc.Get`/`Loc.Format` return the key verbatim, so placeholder assertions check against the resource key string.

- [ ] **Step 1: Write the failing tests**

```csharp
using System;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeDiffDetailViewModelTests
{
    public NodeDiffDetailViewModelTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void Changed_PopulatesBothDefaultSides()
    {
        var vm = new NodeDiffDetailViewModel(42, DiffStatus.Changed,
            defaultLeft: "old", defaultRight: "new", femaleLeft: "", femaleRight: "");

        Assert.Equal("old", vm.DefaultBefore);
        Assert.Equal("new", vm.DefaultAfter);
        Assert.False(vm.IsStructuralOnly);
        Assert.True(vm.ShowTextRows);
    }

    [Fact]
    public void Added_BeforeIsPlaceholder_AfterIsText()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Added,
            defaultLeft: "", defaultRight: "hello", femaleLeft: "", femaleRight: "");

        Assert.Equal("Diff_Detail_NodeAdded", vm.DefaultBefore);
        Assert.Equal("hello", vm.DefaultAfter);
    }

    [Fact]
    public void Removed_AfterIsPlaceholder_BeforeIsText()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Removed,
            defaultLeft: "goodbye", defaultRight: "", femaleLeft: "", femaleRight: "");

        Assert.Equal("goodbye", vm.DefaultBefore);
        Assert.Equal("Diff_Detail_NodeRemoved", vm.DefaultAfter);
    }

    [Fact]
    public void FemaleRow_Hidden_WhenBothFemaleEmpty()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed,
            defaultLeft: "a", defaultRight: "b", femaleLeft: "", femaleRight: "");

        Assert.False(vm.HasFemaleRow);
        Assert.False(vm.ShowFemaleRow);
    }

    [Fact]
    public void FemaleRow_Shown_WhenEitherSideHasFemale()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed,
            defaultLeft: "a", defaultRight: "b", femaleLeft: "", femaleRight: "elle");

        Assert.True(vm.HasFemaleRow);
        Assert.True(vm.ShowFemaleRow);
        Assert.Equal("", vm.FemaleBefore);
        Assert.Equal("elle", vm.FemaleAfter);
    }

    [Fact]
    public void StructuralOnly_True_WhenChangedButTextIdentical()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed,
            defaultLeft: "same", defaultRight: "same", femaleLeft: "f", femaleRight: "f");

        Assert.True(vm.IsStructuralOnly);
        Assert.False(vm.ShowTextRows);
    }

    [Fact]
    public void StructuralOnly_False_WhenFemaleDiffers()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed,
            defaultLeft: "same", defaultRight: "same", femaleLeft: "f1", femaleRight: "f2");

        Assert.False(vm.IsStructuralOnly);
        Assert.True(vm.HasFemaleRow);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeDiffDetailViewModelTests"
```

Expected: compile error — `NodeDiffDetailViewModel` does not exist yet.

- [ ] **Step 3: Implement `NodeDiffDetailViewModel`**

```csharp
using System;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

/// Before/after text detail for one selected diff node. Pure presentation logic:
/// placeholder substitution for added/removed nodes, female-row visibility, and
/// structural-only detection. The inline word-level highlighting is rendered by
/// the view from the Before/After strings exposed here (see DiffWindow).
public class NodeDiffDetailViewModel
{
    public int        NodeId { get; }
    public DiffStatus Kind   { get; }

    public string DefaultBefore { get; }
    public string DefaultAfter  { get; }
    public string FemaleBefore  { get; }
    public string FemaleAfter   { get; }

    public bool HasFemaleRow     { get; }
    public bool IsStructuralOnly { get; }

    public bool ShowTextRows  => !IsStructuralOnly;
    public bool ShowFemaleRow => ShowTextRows && HasFemaleRow;

    public string HeaderText => Loc.Format("Diff_Detail_Header", NodeId);

    public NodeDiffDetailViewModel(
        int nodeId, DiffStatus kind,
        string defaultLeft, string defaultRight,
        string femaleLeft,  string femaleRight)
    {
        NodeId = nodeId;
        Kind   = kind;

        DefaultBefore = kind == DiffStatus.Added
            ? Loc.Get("Diff_Detail_NodeAdded")   : defaultLeft;
        DefaultAfter  = kind == DiffStatus.Removed
            ? Loc.Get("Diff_Detail_NodeRemoved") : defaultRight;

        // Female-row visibility is judged on the real text only, so a placeholder
        // side does not by itself force the row open.
        HasFemaleRow = !string.IsNullOrEmpty(femaleLeft)
                    || !string.IsNullOrEmpty(femaleRight);

        FemaleBefore = kind == DiffStatus.Added
            ? Loc.Get("Diff_Detail_NodeAdded")   : femaleLeft;
        FemaleAfter  = kind == DiffStatus.Removed
            ? Loc.Get("Diff_Detail_NodeRemoved") : femaleRight;

        IsStructuralOnly = kind == DiffStatus.Changed
            && string.Equals(defaultLeft, defaultRight, StringComparison.Ordinal)
            && string.Equals(femaleLeft,  femaleRight,  StringComparison.Ordinal);
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeDiffDetailViewModelTests"
```

Expected: 7 passed.

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/ViewModels/NodeDiffDetailViewModel.cs DialogEditor.Tests/ViewModels/NodeDiffDetailViewModelTests.cs
git commit -m "feat: NodeDiffDetailViewModel — before/after text with placeholder and structural-only logic"
```

---

### Task 2: Wire selection → `SelectedNodeDetail` in `DiffViewModel`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`
- Modify: `DialogEditor.Tests/ViewModels/DiffViewModelTests.cs`

`DiffViewModel` caches `nodeId → (default, female)` text for both endpoints while building the canvas (reusing the left reconstruction that already runs for ghosting), subscribes to the canvas's `SelectedNode`, and builds `SelectedNodeDetail`. Unchanged nodes and Applied-Preview mode produce a null detail (panel hidden).

- [ ] **Step 1: Write the failing tests**

Add these members to the existing `DiffViewModelTests` class (a text-carrying node helper plus three tests):

```csharp
    private static NodeEditSnapshot NodeT(int id, string text, string female = "") =>
        new(id, false, SpeakerCategory.Npc, "", "", text, female, "Conversation", "None", "", "", "", false, false, [], [], []);

    [Fact]
    public void SelectingAddedNode_PopulatesDetail_WithPlaceholderBefore()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var snap     = new ConversationEditSnapshot([NodeT(1, "Hi"), NodeT(2, "Added line")]);
        var provider = new StubProvider(file, snap);

        var diskProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [NodeT(1, "Hi")], [], []));
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [NodeT(1, "Hi"), NodeT(2, "Added line")], [], []));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refProject), branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        vm.Selected = vm.Changes.Single(c => c.Name == convName);

        var node2 = vm.DiffCanvas!.Nodes.First(n => n.NodeId == 2);
        vm.DiffCanvas.SelectedNode = node2;

        Assert.NotNull(vm.SelectedNodeDetail);
        Assert.Equal(2, vm.SelectedNodeDetail!.NodeId);
        Assert.Equal(DiffStatus.Added, vm.SelectedNodeDetail.Kind);
        Assert.Equal("Diff_Detail_NodeAdded", vm.SelectedNodeDetail.DefaultBefore);
        Assert.Equal(node2.DefaultText, vm.SelectedNodeDetail.DefaultAfter);
    }

    [Fact]
    public void SelectingChangedNode_PopulatesBothSides_FromTheirReconstructions()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var snap     = new ConversationEditSnapshot([NodeT(1, "base")]);
        var provider = new StubProvider(file, snap);

        var diskProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [NodeT(1, "old text")], [], []));
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [NodeT(1, "new text")], [], []));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refProject), branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        vm.Selected = vm.Changes.Single(c => c.Name == convName);

        var node1 = vm.DiffCanvas!.Nodes.First(n => n.NodeId == 1);
        vm.DiffCanvas.SelectedNode = node1;

        Assert.NotNull(vm.SelectedNodeDetail);
        Assert.Equal(DiffStatus.Changed, vm.SelectedNodeDetail!.Kind);
        Assert.Equal(node1.DefaultText, vm.SelectedNodeDetail.DefaultAfter);
        Assert.NotEqual(vm.SelectedNodeDetail.DefaultBefore, vm.SelectedNodeDetail.DefaultAfter);
        Assert.NotEqual("Diff_Detail_NodeAdded", vm.SelectedNodeDetail.DefaultBefore);
    }

    [Fact]
    public void SelectingUnchangedNode_LeavesDetailNull()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var snap     = new ConversationEditSnapshot([NodeT(1, "Hi"), NodeT(2, "Added line")]);
        var provider = new StubProvider(file, snap);

        var diskProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [NodeT(1, "Hi")], [], []));
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [NodeT(1, "Hi"), NodeT(2, "Added line")], [], []));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refProject), branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        vm.Selected = vm.Changes.Single(c => c.Name == convName);

        // Node 1 is identical on both sides → Unchanged → no detail panel.
        var node1 = vm.DiffCanvas!.Nodes.First(n => n.NodeId == 1);
        vm.DiffCanvas.SelectedNode = node1;

        Assert.Null(vm.SelectedNodeDetail);
    }
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffViewModelTests"
```

Expected: compile error — `DiffViewModel.SelectedNodeDetail` does not exist yet.

- [ ] **Step 3: Add the observable property and caching fields**

In `DiffViewModel.cs`, add alongside the other `[ObservableProperty]` fields (near `_selectedGroup`, around line 40):

```csharp
    [ObservableProperty] private NodeDiffDetailViewModel? _selectedNodeDetail;
```

Add these private fields next to the other private fields (near `_leftProject` / `_rightProject`, around line 27):

```csharp
    private readonly Dictionary<int, (string Default, string Female)> _leftTextById  = new();
    private readonly Dictionary<int, (string Default, string Female)> _rightTextById = new();
    private ConversationViewModel? _observedCanvas;
```

- [ ] **Step 4: Add the selection-observation helpers**

Add these three methods to `DiffViewModel` (e.g. directly above `BuildDiffCanvas`):

```csharp
    private void ObserveCanvas(ConversationViewModel? canvas)
    {
        if (_observedCanvas is not null)
            _observedCanvas.PropertyChanged -= OnCanvasPropertyChanged;
        _observedCanvas = canvas;
        if (_observedCanvas is not null)
            _observedCanvas.PropertyChanged += OnCanvasPropertyChanged;
    }

    private void OnCanvasPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ConversationViewModel.SelectedNode))
            UpdateSelectedNodeDetail();
    }

    private void UpdateSelectedNodeDetail()
    {
        var node = DiffCanvas?.SelectedNode;
        if (CanvasMode != CanvasMode.Changes || node is null
            || node.DiffStatus == DiffStatus.Unchanged)
        {
            SelectedNodeDetail = null;
            return;
        }

        var (dl, fl) = _leftTextById.GetValueOrDefault(node.NodeId, ("", ""));
        var (dr, fr) = _rightTextById.GetValueOrDefault(node.NodeId, ("", ""));
        SelectedNodeDetail = new NodeDiffDetailViewModel(node.NodeId, node.DiffStatus, dl, dr, fl, fr);
    }
```

- [ ] **Step 5: Replace `BuildDiffCanvas` with the text-map-caching version**

Replace the entire existing `BuildDiffCanvas()` method (from `private void BuildDiffCanvas()` through its closing brace) with:

```csharp
    private void BuildDiffCanvas()
    {
        SelectedNodeDetail = null;

        if (Selected is null)
        {
            ObserveCanvas(null);
            DiffCanvas  = null;
            CanvasHint  = "";
            return;
        }

        if (_provider is null)
        {
            ObserveCanvas(null);
            DiffCanvas  = null;
            CanvasHint  = Loc.Get("DiffWindow_NoGameFolder");
            return;
        }

        if (CanvasMode == CanvasMode.AppliedPreview && WorkingCopyIsEndpoint
            && TargetProject is not null && SourceProject is not null)
        {
            BuildAppliedPreviewCanvas();
            return;
        }

        try
        {
            var name = Selected.Name;

            // ── Reconstruct the RIGHT (new) conversation ──────────────────
            Conversation rightConv = ReconstructConversation(name, _rightProject, _provider);

            var vm = new ConversationViewModel(_dispatcher);
            vm.Load(rightConv);
            vm.IsEditable = false;

            // ── Tint nodes according to diff ──────────────────────────────
            var addedSet    = Selected.Added.ToHashSet();
            var modifiedSet = Selected.Modified.ToHashSet();
            var removedSet  = Selected.Removed.ToHashSet();

            foreach (var node in vm.Nodes)
            {
                if (addedSet.Contains(node.NodeId))
                    node.DiffStatus = DiffStatus.Added;
                else if (modifiedSet.Contains(node.NodeId))
                    node.DiffStatus = DiffStatus.Changed;
                // NOTE: ProjectDiff is patch-relative (see ProjectDiff remarks). A node
                // present here but flagged Removed has "reverted to base" between the two
                // versions rather than being deleted; it is tinted Removed by design.
                else if (removedSet.Contains(node.NodeId))
                    node.DiffStatus = DiffStatus.Removed;
            }

            // ── Cache right-side text for the before/after detail panel ────
            _rightTextById.Clear();
            foreach (var n in rightConv.Nodes)
            {
                var e = rightConv.Strings.Get(n.NodeId);
                _rightTextById[n.NodeId] = (e?.DefaultText ?? "", e?.FemaleText ?? "");
            }

            // ── Reconstruct the LEFT (old) conversation once: feeds both the
            //    left text cache and the ghost-removed-node injection ────────
            _leftTextById.Clear();
            try
            {
                Conversation leftConv = ReconstructConversation(name, _leftProject, _provider);

                foreach (var n in leftConv.Nodes)
                {
                    var e = leftConv.Strings.Get(n.NodeId);
                    _leftTextById[n.NodeId] = (e?.DefaultText ?? "", e?.FemaleText ?? "");
                }

                // ── Ghost removed nodes (from the left / old project) ──────
                if (removedSet.Count > 0)
                {
                    foreach (var leftNode in leftConv.Nodes)
                    {
                        if (!removedSet.Contains(leftNode.NodeId)) continue;
                        // Only inject if not already present (removed nodes are absent from right)
                        if (vm.Nodes.Any(n => n.NodeId == leftNode.NodeId)) continue;

                        var entry   = leftConv.Strings.Get(leftNode.NodeId);
                        var ghost   = new NodeViewModel(leftNode, entry);
                        ghost.OnSelected   = n => vm.SelectedNode = n;
                        ghost.Input.Owner  = ghost;
                        ghost.Output.Owner = ghost;
                        ghost.DiffStatus   = DiffStatus.Removed;
                        vm.Nodes.Add(ghost);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"DiffViewModel: could not reconstruct left conversation for '{name}': {ex.Message}");
            }

            DiffCanvas = vm;
            CanvasHint = "";
            ObserveCanvas(vm);
            UpdateSelectedNodeDetail();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DiffViewModel: BuildDiffCanvas failed for '{Selected?.Name}': {ex.Message}");
            ObserveCanvas(null);
            DiffCanvas  = null;
            CanvasHint  = Loc.Get("DiffWindow_CanvasError");
        }
    }
```

- [ ] **Step 6: Detach the panel in Applied-Preview mode**

In `BuildAppliedPreviewCanvas()`, add `SelectedNodeDetail = null;` as the first statement inside the method, and add `ObserveCanvas(null);` immediately after the existing `DiffCanvas = vm;` line in its `try` block. The existing code reads:

```csharp
            DiffCanvas = vm;
            CanvasHint = "";
```

Change it to:

```csharp
            DiffCanvas = vm;
            CanvasHint = "";
            ObserveCanvas(null);
```

This keeps the detail panel hidden while previewing (different before/after semantics) and stops observing the stale changes-canvas.

- [ ] **Step 7: Run tests to confirm they pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffViewModelTests"
```

Expected: all `DiffViewModelTests` pass (existing + 3 new).

- [ ] **Step 8: Commit**

```
git add DialogEditor.ViewModels/ViewModels/DiffViewModel.cs DialogEditor.Tests/ViewModels/DiffViewModelTests.cs
git commit -m "feat: DiffViewModel surfaces SelectedNodeDetail from canvas node selection"
```

---

### Task 3: Localized strings

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

Add the new keys alongside the other `Diff_` keys (search for an existing key such as `Diff_LegendChanged` or `DiffWindow_Title` to find the block). No test — these are consumed by Tasks 4's view and verified by the headless tests there.

- [ ] **Step 1: Add the keys**

```xml
    <!-- ─── Diff: before/after node detail panel ──────────────────────── -->
    <sys:String x:Key="Diff_Detail_Header">Node {0}</sys:String>
    <sys:String x:Key="Diff_Detail_BeforeLabel">Before</sys:String>
    <sys:String x:Key="Diff_Detail_AfterLabel">After</sys:String>
    <sys:String x:Key="Diff_Detail_DefaultTextLabel">Default text</sys:String>
    <sys:String x:Key="Diff_Detail_FemaleTextLabel">Female text</sys:String>
    <sys:String x:Key="Diff_Detail_NodeAdded">(node added — no previous text)</sys:String>
    <sys:String x:Key="Diff_Detail_NodeRemoved">(node removed)</sys:String>
    <sys:String x:Key="Diff_Detail_StructuralOnly">No text change — only structural fields (conditions, scripts, or speaker) differ.</sys:String>
    <sys:String x:Key="ToolTip_Diff_Detail">Shows the selected node's text before and after the change. Added or removed text is highlighted; the left value is the version on the left endpoint, the right value the version on the right endpoint.</sys:String>
```

- [ ] **Step 2: Build to verify the resource dictionary still parses**

```
dotnet build DialogEditor.Avalonia
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: localized strings for diff before/after detail panel"
```

---

### Task 4: Detail panel in `DiffWindow` + inline rendering + headless tests

**Files:**
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml.cs`
- Modify: `DialogEditor.Tests/Views/DiffWindowTests.cs`

The panel docks to the bottom of the **right-hand canvas column** (so the left conversation list keeps full height). Code-behind fills the four named `TextBlock`s with coloured `Run`s via `TextDiff`, reusing the `GitConflictResolutionWindow` rendering pattern with Before/After brushes.

- [ ] **Step 1: Write the failing headless tests**

Add a text-carrying node helper and two tests to the existing `DiffWindowTests` class, and add these `using`s at the top of the file if not already present: `using Avalonia.Controls;` (already present), `using DialogEditor.Core.Editing;`, `using DialogEditor.Core.GameData;`.

```csharp
    private static NodeEditSnapshot NodeT(int id, string text, string female = "") =>
        new(id, false, SpeakerCategory.Npc, "", "", text, female, "Conversation", "None", "", "", "", false, false, [], [], []);

    [AvaloniaFact]
    public void DetailPanel_Hidden_WhenNoNodeSelected()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var snap     = new ConversationEditSnapshot([NodeT(1, "base")]);
        var provider = new StubProvider(file, snap);

        var diskProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [NodeT(1, "old")], [], []));
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [NodeT(1, "new")], [], []));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refProject));

        var vm     = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        var window = new DiffWindow(vm);
        window.Show();

        Assert.False(window.FindControl<Border>("DetailPanel")!.IsVisible);
    }

    [AvaloniaFact]
    public void DetailPanel_Visible_AfterSelectingChangedNode()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var snap     = new ConversationEditSnapshot([NodeT(1, "base")]);
        var provider = new StubProvider(file, snap);

        var diskProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [NodeT(1, "old")], [], []));
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [NodeT(1, "new")], [], []));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refProject));

        var vm     = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        var window = new DiffWindow(vm);
        window.Show();

        vm.Selected = vm.Changes.Single(c => c.Name == convName);
        vm.DiffCanvas!.SelectedNode = vm.DiffCanvas.Nodes.First(n => n.NodeId == 1);

        Assert.True(window.FindControl<Border>("DetailPanel")!.IsVisible);
    }
```

`StubProvider`, `StubDispatcher`, `ConversationPatch`, `DialogProject`, and `DialogProjectSerializer` are already imported by the existing test file's usings (`DialogEditor.Tests.Helpers`, `DialogEditor.Patch`, `DialogEditor.Patch.Diff`, `DialogEditor.ViewModels`).

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffWindowTests"
```

Expected: compile/lookup failure — there is no control named `DetailPanel` yet.

- [ ] **Step 3: Add the detail panel to `DiffWindow.axaml`**

Inside `<DockPanel Grid.Column="2">`, immediately **after** the closing `</StackPanel>` of the canvas-mode toggle row (the one containing the `RadioButton`s and colour key) and **before** the `<!-- Canvas panel -->` `<Panel>`, insert:

```xml
                <!-- ── Before/after detail panel (shown when a node is selected) ── -->
                <Border DockPanel.Dock="Bottom"
                        x:Name="DetailPanel"
                        Background="#1e1e1e"
                        BorderBrush="#444" BorderThickness="1" CornerRadius="4"
                        Padding="8" Margin="0,6,0,0"
                        IsVisible="{Binding SelectedNodeDetail, Converter={StaticResource IsNotNull}}"
                        ToolTip.Tip="{StaticResource ToolTip_Diff_Detail}">
                    <StackPanel Spacing="4" DataContext="{Binding SelectedNodeDetail}">
                        <TextBlock Text="{Binding HeaderText}" Foreground="#ddd" FontWeight="Bold" FontSize="12"/>

                        <!-- Structural-only hint -->
                        <TextBlock Text="{StaticResource Diff_Detail_StructuralOnly}"
                                   Foreground="#e0a030" FontSize="11" TextWrapping="Wrap"
                                   IsVisible="{Binding IsStructuralOnly}"/>

                        <!-- Default text before/after -->
                        <StackPanel Spacing="2" IsVisible="{Binding ShowTextRows}">
                            <TextBlock Text="{StaticResource Diff_Detail_DefaultTextLabel}" Foreground="#888" FontSize="10"/>
                            <Grid ColumnDefinitions="Auto,*">
                                <TextBlock Grid.Column="0" Text="{StaticResource Diff_Detail_BeforeLabel}" Foreground="#888" FontSize="11" Margin="0,0,6,0"/>
                                <TextBlock Grid.Column="1" x:Name="DefaultBeforeText" Foreground="#ddd" FontSize="12" TextWrapping="Wrap"/>
                            </Grid>
                            <Grid ColumnDefinitions="Auto,*">
                                <TextBlock Grid.Column="0" Text="{StaticResource Diff_Detail_AfterLabel}" Foreground="#888" FontSize="11" Margin="0,0,6,0"/>
                                <TextBlock Grid.Column="1" x:Name="DefaultAfterText" Foreground="#ddd" FontSize="12" TextWrapping="Wrap"/>
                            </Grid>
                        </StackPanel>

                        <!-- Female text before/after -->
                        <StackPanel Spacing="2" IsVisible="{Binding ShowFemaleRow}">
                            <TextBlock Text="{StaticResource Diff_Detail_FemaleTextLabel}" Foreground="#888" FontSize="10"/>
                            <Grid ColumnDefinitions="Auto,*">
                                <TextBlock Grid.Column="0" Text="{StaticResource Diff_Detail_BeforeLabel}" Foreground="#888" FontSize="11" Margin="0,0,6,0"/>
                                <TextBlock Grid.Column="1" x:Name="FemaleBeforeText" Foreground="#ddd" FontSize="12" TextWrapping="Wrap"/>
                            </Grid>
                            <Grid ColumnDefinitions="Auto,*">
                                <TextBlock Grid.Column="0" Text="{StaticResource Diff_Detail_AfterLabel}" Foreground="#888" FontSize="11" Margin="0,0,6,0"/>
                                <TextBlock Grid.Column="1" x:Name="FemaleAfterText" Foreground="#ddd" FontSize="12" TextWrapping="Wrap"/>
                            </Grid>
                        </StackPanel>
                    </StackPanel>
                </Border>
```

- [ ] **Step 4: Replace `DiffWindow.axaml.cs` with the inline-rendering version**

```csharp
using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using DialogEditor.Patch.GitConflict;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class DiffWindow : Window
{
    private static readonly IBrush CommonBrush = new SolidColorBrush(Color.Parse("#e8e8e8"));
    private static readonly IBrush BeforeBrush = new SolidColorBrush(Color.Parse("#9be39b"));
    private static readonly IBrush AfterBrush  = new SolidColorBrush(Color.Parse("#ff9c9c"));

    private DiffViewModel? _vm;
    private DiffHelpWindow? _helpWindow;

    public DiffWindow() => InitializeComponent();

    public DiffWindow(DiffViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffViewModel.SelectedNodeDetail))
            UpdateDetail(_vm?.SelectedNodeDetail);
    }

    // Render the selected node's before/after text with the changed run highlighted.
    // Structural-only changes show the hint (bound in XAML) and clear the text rows.
    private void UpdateDetail(NodeDiffDetailViewModel? d)
    {
        if (d is null || d.IsStructuralOnly)
        {
            DefaultBeforeText.Inlines = new InlineCollection();
            DefaultAfterText.Inlines  = new InlineCollection();
            FemaleBeforeText.Inlines  = new InlineCollection();
            FemaleAfterText.Inlines   = new InlineCollection();
            return;
        }

        RenderPair(DefaultBeforeText, DefaultAfterText, d.DefaultBefore, d.DefaultAfter);

        if (d.HasFemaleRow)
            RenderPair(FemaleBeforeText, FemaleAfterText, d.FemaleBefore, d.FemaleAfter);
        else
        {
            FemaleBeforeText.Inlines = new InlineCollection();
            FemaleAfterText.Inlines  = new InlineCollection();
        }
    }

    private static void RenderPair(TextBlock beforeBlock, TextBlock afterBlock, string before, string after)
    {
        var beforeInlines = new InlineCollection();
        var afterInlines  = new InlineCollection();

        foreach (var span in TextDiff.Diff(before, after))
        {
            switch (span.Kind)
            {
                case DiffKind.Common:
                    beforeInlines.Add(MakeRun(span.Text, CommonBrush));
                    afterInlines.Add(MakeRun(span.Text, CommonBrush));
                    break;
                case DiffKind.MineOnly:
                    beforeInlines.Add(MakeRun(span.Text, BeforeBrush));
                    break;
                case DiffKind.TheirsOnly:
                    afterInlines.Add(MakeRun(span.Text, AfterBrush));
                    break;
            }
        }

        beforeBlock.Inlines = beforeInlines;
        afterBlock.Inlines  = afterInlines;
    }

    private static Run MakeRun(string text, IBrush brush) => new(text) { Foreground = brush };

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        if (_helpWindow is null || !_helpWindow.IsVisible)
            _helpWindow = new DiffHelpWindow();
        _helpWindow.Show();
        _helpWindow.Activate();
    }

    private void UndoBringIn_Click(object? sender, RoutedEventArgs e)
        => (DataContext as DiffViewModel)?.RequestUndoApply?.Invoke();
}
```

- [ ] **Step 5: Build**

```
dotnet build DialogEditor.Avalonia
```

Expected: `Build succeeded. 0 Error(s)`. Fix any missing resource-key or control-name errors before continuing.

- [ ] **Step 6: Run the headless tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffWindowTests"
```

Expected: all `DiffWindowTests` pass (existing + 2 new).

- [ ] **Step 7: Commit**

```
git add DialogEditor.Avalonia/Views/DiffWindow.axaml DialogEditor.Avalonia/Views/DiffWindow.axaml.cs DialogEditor.Tests/Views/DiffWindowTests.cs
git commit -m "feat: diff before/after detail panel with word-level inline highlighting"
```

---

### Task 5: Update Gaps.md and full verification

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Update the diff-viewing gap text**

In `Gaps.md`, find the sentence in the **Diff viewing** paragraph:

> Before/after text detail for a selected node is not yet implemented (deferred — the canvas tinting is the priority).

Replace it with:

> Before/after text detail for a selected node is implemented: selecting a node shows its Default/Female text before and after, with word-level inline highlighting (reusing `TextDiff`). Hidden in Applied-Preview mode; a node whose only differences are structural shows a "structural only" hint instead of identical text. Multi-language before/after (showing every changed language at once) remains a deferred follow-up — the panel currently uses the diff window's selected language.

- [ ] **Step 2: Run the full test suite**

```
dotnet test DialogEditor.Tests
```

Expected: all tests pass (0 failures).

- [ ] **Step 3: Build the whole solution**

```
dotnet build
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4: Commit**

```
git add Gaps.md
git commit -m "docs: record diff before/after text detail as implemented"
```

---

## Verification Checklist

After all tasks complete:

1. `dotnet test DialogEditor.Tests` — all tests pass (0 failures).
2. `dotnet build` — 0 errors.
3. **Manual**: open the app with a game folder loaded, open the compare/diff window, pick two endpoints that differ, select a changed conversation, then click a **changed** node — the bottom panel shows Default text Before/After with the changed words highlighted.
4. **Manual**: click an **added** node — Before shows "(node added — no previous text)", After shows the new text.
5. **Manual**: click a **removed** (ghost) node — Before shows the old text, After shows "(node removed)".
6. **Manual**: click an **unchanged** node — the panel disappears.
7. **Manual**: a node with a female-text variant shows the Female text row; one without does not.
8. **Manual**: switch to **Applied Preview** mode — the detail panel is hidden.
9. **Manual**: a node whose only change is conditions/scripts shows the "structural only" hint, not two identical lines.
