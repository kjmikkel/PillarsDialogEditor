# Automatic Dependency-Pulling Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When the user ticks a changed node to bring in, auto-tick the added nodes it links to (transitively), with a default-on toggle.

**Architecture:** A pure `DependencyClosure` computes the transitive set of added link-targets. `ConversationChangeViewModel` runs it when one of its nodes is ticked (gated by an `AutoPullEnabled` flag). `DiffViewModel` builds each conversation's outgoing-link map from the bring-in source and owns the toggle. Outgoing-only, intra-conversation, added targets only.

**Tech Stack:** C# 12 / .NET 8, CommunityToolkit.Mvvm, Avalonia 11.3.14, xUnit + `Avalonia.Headless.XUnit`.

**Spec:** `docs/superpowers/specs/2026-06-04-dependency-pulling-design.md`

---

## File Structure

- **Create** `DialogEditor.Patch/Diff/DependencyClosure.cs` — pure transitive closure.
- **Create** `DialogEditor.Tests/Patch/Diff/DependencyClosureTests.cs`.
- **Modify** `DialogEditor.ViewModels/ViewModels/ConversationChangeViewModel.cs` — dependency data + auto-pull on tick.
- **Modify** `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` — `AutoPullDependencies` toggle; build/pass dependency data in `Recompute`.
- **Modify** `DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs` — group-level + integration tests.
- **Modify** `DialogEditor.Avalonia/Resources/Strings.axaml` — toggle label + tooltip.
- **Modify** `DialogEditor.Avalonia/Views/DiffWindow.axaml` — the checkbox.
- **Modify** `DialogEditor.Tests/Views/DiffWindowTests.cs` — checkbox binding test.
- **Modify** `Gaps.md`.

Tasks build in order (2 uses 1; 3 uses 2; 4 uses 3's property); each is green.

---

### Task 1: `DependencyClosure` (pure) and tests

**Files:**
- Create: `DialogEditor.Tests/Patch/Diff/DependencyClosureTests.cs`
- Create: `DialogEditor.Patch/Diff/DependencyClosure.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Collections.Generic;
using DialogEditor.Patch.Diff;

namespace DialogEditor.Tests.Patch.Diff;

public class DependencyClosureTests
{
    private static Dictionary<int, IReadOnlyList<int>> Edges(
        params (int From, int[] To)[] items) =>
        items.ToDictionary(i => i.From, i => (IReadOnlyList<int>)i.To);

    [Fact]
    public void SingleEdge_PullsTarget()
    {
        var result = DependencyClosure.Expand(1, Edges((1, [2])), new HashSet<int> { 2 });
        Assert.Equal(new HashSet<int> { 2 }, result);
    }

    [Fact]
    public void TransitiveChain_PullsAllReachableAddedTargets()
    {
        var result = DependencyClosure.Expand(1, Edges((1, [2]), (2, [3])), new HashSet<int> { 2, 3 });
        Assert.Equal(new HashSet<int> { 2, 3 }, result);
    }

    [Fact]
    public void TargetsNotInAddedIds_AreExcluded()
    {
        // 3 is a link target but not an added node → not pulled.
        var result = DependencyClosure.Expand(1, Edges((1, [2, 3])), new HashSet<int> { 2 });
        Assert.Equal(new HashSet<int> { 2 }, result);
    }

    [Fact]
    public void Cycle_Terminates()
    {
        var result = DependencyClosure.Expand(1, Edges((1, [2]), (2, [1])), new HashSet<int> { 1, 2 });
        Assert.Equal(new HashSet<int> { 2 }, result); // start (1) is never added to the result
    }

    [Fact]
    public void NoQualifyingTargets_ReturnsEmpty()
    {
        Assert.Empty(DependencyClosure.Expand(1, Edges((1, [])), new HashSet<int>()));
        Assert.Empty(DependencyClosure.Expand(1, new Dictionary<int, IReadOnlyList<int>>(), new HashSet<int> { 2 }));
    }
}
```

- [ ] **Step 2: Run, confirm fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DependencyClosureTests"
```
Expected: compile error — `DependencyClosure` does not exist.

- [ ] **Step 3: Implement**

```csharp
namespace DialogEditor.Patch.Diff;

/// Transitive closure of a node's outgoing link targets, restricted to nodes that
/// are *added* (would not otherwise exist after a selective apply). Used to pull a
/// brought-in node's added link targets so its links don't point at nodes that were
/// never created. Cycle-safe; the start node is never part of the result.
public static class DependencyClosure
{
    public static IReadOnlySet<int> Expand(
        int start,
        IReadOnlyDictionary<int, IReadOnlyList<int>> outgoing,
        IReadOnlySet<int> addedIds)
    {
        var result  = new HashSet<int>();
        var visited = new HashSet<int> { start };
        var stack   = new Stack<int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!outgoing.TryGetValue(node, out var targets)) continue;

            foreach (var t in targets)
            {
                if (!addedIds.Contains(t)) continue; // only pull added targets
                if (!visited.Add(t)) continue;       // cycle / duplicate guard
                result.Add(t);
                stack.Push(t);                        // follow transitively
            }
        }

        return result;
    }
}
```

- [ ] **Step 4: Run, confirm pass** (`...DependencyClosureTests`) — expect 5 passed.

- [ ] **Step 5: Commit**

```
git add DialogEditor.Patch/Diff/DependencyClosure.cs DialogEditor.Tests/Patch/Diff/DependencyClosureTests.cs
git commit -m "feat: DependencyClosure — transitive added-link-target closure"
```

---

### Task 2: Auto-pull in `ConversationChangeViewModel`

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationChangeViewModel.cs`

The group receives its conversation's `outgoing` map + `addedIds` and an `AutoPullEnabled` flag. When a node is ticked and the flag is on, it runs `DependencyClosure.Expand` from that node and ticks the results under the existing `_suppressRollDown` guard (one `SelectionChanged`, no event storm, no recursion).

- [ ] **Step 1: Write the failing tests**

Add to `DiffViewModelApplyTests` (these test the group in isolation):

```csharp
    private static ConversationChangeViewModel MakeGroupWithDeps(
        ConversationChange change,
        IReadOnlyDictionary<int, IReadOnlyList<int>> outgoing,
        IReadOnlySet<int> addedIds,
        bool autoPull = true)
    {
        var g = new ConversationChangeViewModel(change);
        g.SetDependencies(outgoing, addedIds);
        g.AutoPullEnabled = autoPull;
        return g;
    }

    [Fact]
    public void Group_TickingAddedNode_AutoTicksLinkedAddedNode()
    {
        var change = new ConversationChange("c", Added: [1, 2], Removed: [], Modified: []);
        var g = MakeGroupWithDeps(change,
            new Dictionary<int, IReadOnlyList<int>> { [1] = [2] }, new HashSet<int> { 1, 2 });

        g.Nodes.First(n => n.NodeId == 1).IsSelected = true;

        Assert.True(g.Nodes.First(n => n.NodeId == 2).IsSelected);
    }

    [Fact]
    public void Group_AutoPull_IsTransitive()
    {
        var change = new ConversationChange("c", Added: [1, 2, 3], Removed: [], Modified: []);
        var g = MakeGroupWithDeps(change,
            new Dictionary<int, IReadOnlyList<int>> { [1] = [2], [2] = [3] }, new HashSet<int> { 1, 2, 3 });

        g.Nodes.First(n => n.NodeId == 1).IsSelected = true;

        Assert.True(g.Nodes.First(n => n.NodeId == 2).IsSelected);
        Assert.True(g.Nodes.First(n => n.NodeId == 3).IsSelected);
    }

    [Fact]
    public void Group_AutoPullOff_DoesNotPull()
    {
        var change = new ConversationChange("c", Added: [1, 2], Removed: [], Modified: []);
        var g = MakeGroupWithDeps(change,
            new Dictionary<int, IReadOnlyList<int>> { [1] = [2] }, new HashSet<int> { 1, 2 }, autoPull: false);

        g.Nodes.First(n => n.NodeId == 1).IsSelected = true;

        Assert.False(g.Nodes.First(n => n.NodeId == 2).IsSelected);
    }

    [Fact]
    public void Group_DoesNotPull_ModifiedOrRemovedTargets()
    {
        // Node 1 (added) links to 4 (modified) and 5 (removed); neither is in addedIds.
        var change = new ConversationChange("c", Added: [1], Removed: [5], Modified: [4]);
        var g = MakeGroupWithDeps(change,
            new Dictionary<int, IReadOnlyList<int>> { [1] = [4, 5] }, new HashSet<int> { 1 });

        g.Nodes.First(n => n.NodeId == 1).IsSelected = true;

        Assert.False(g.Nodes.First(n => n.NodeId == 4).IsSelected);
        Assert.False(g.Nodes.First(n => n.NodeId == 5).IsSelected);
    }

    [Fact]
    public void Group_UntickingSource_LeavesDependenciesTicked()
    {
        var change = new ConversationChange("c", Added: [1, 2], Removed: [], Modified: []);
        var g = MakeGroupWithDeps(change,
            new Dictionary<int, IReadOnlyList<int>> { [1] = [2] }, new HashSet<int> { 1, 2 });

        var n1 = g.Nodes.First(n => n.NodeId == 1);
        n1.IsSelected = true;        // pulls 2
        n1.IsSelected = false;       // untick source

        Assert.True(g.Nodes.First(n => n.NodeId == 2).IsSelected); // dependency stays
    }
```

- [ ] **Step 2: Run, confirm fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffViewModelApplyTests"
```
Expected: compile error — `SetDependencies` / `AutoPullEnabled` do not exist.

- [ ] **Step 3: Add dependency data + auto-pull to `ConversationChangeViewModel`**

Replace the current `Add` method and `OnNodeSelectionChanged` method, and add the new members. The full updated file body (after the existing `IsAllSelected` property) is:

Add these fields/members near the top of the class (after `private bool _suppressRollDown;`):

```csharp
    private IReadOnlyDictionary<int, IReadOnlyList<int>> _outgoing =
        new Dictionary<int, IReadOnlyList<int>>();
    private IReadOnlySet<int> _addedIds = new HashSet<int>();

    /// When true, ticking a node also ticks the added nodes it links to (transitively).
    public bool AutoPullEnabled { get; set; }

    /// Supplies the conversation's outgoing-link map (source node id → target node ids)
    /// and the set of added node ids eligible to be auto-pulled.
    public void SetDependencies(
        IReadOnlyDictionary<int, IReadOnlyList<int>> outgoing, IReadOnlySet<int> addedIds)
    {
        _outgoing = outgoing;
        _addedIds = addedIds;
    }
```

Change `Add` so each node's event carries the node:

```csharp
    private void Add(int id, DiffStatus kind)
    {
        var node = new NodeChangeViewModel(id, kind);
        node.SelectionChanged += () => OnNodeSelectionChanged(node);
        Nodes.Add(node);
    }
```

Replace `OnNodeSelectionChanged()` with the node-aware version plus the pull helper:

```csharp
    private void OnNodeSelectionChanged(NodeChangeViewModel node)
    {
        if (_suppressRollDown) return;

        if (node.IsSelected && AutoPullEnabled)
            PullDependencies(node.NodeId);

        OnPropertyChanged(nameof(IsAllSelected));
        SelectionChanged?.Invoke();
    }

    private void PullDependencies(int startNodeId)
    {
        var toSelect = DependencyClosure.Expand(startNodeId, _outgoing, _addedIds);
        if (toSelect.Count == 0) return;

        _suppressRollDown = true;
        foreach (var n in Nodes)
            if (toSelect.Contains(n.NodeId))
                n.IsSelected = true;
        _suppressRollDown = false;
    }
```

(`DependencyClosure` is in `DialogEditor.Patch.Diff`, already imported via the existing `using DialogEditor.Patch.Diff;` at the top of the file.)

- [ ] **Step 4: Run, confirm pass** (`...DiffViewModelApplyTests`) — expect all pass (existing + 5 new).

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/ViewModels/ConversationChangeViewModel.cs DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs
git commit -m "feat: ConversationChangeViewModel auto-pulls linked added nodes on tick"
```

---

### Task 3: Toggle + dependency wiring in `DiffViewModel`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`
- Modify: `DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs`

`DiffViewModel` owns the `AutoPullDependencies` toggle, builds each conversation's outgoing-link map from `SourceProject`, and hands it to the groups in `Recompute`.

- [ ] **Step 1: Write the failing tests**

Add a linked-add fixture and two integration tests to `DiffViewModelApplyTests`:

```csharp
    // Source (ref/right) adds node 5 (which links to added node 6) and node 6.
    // Target (working copy/left) has neither → both are Added; ticking 5 pulls 6.
    private DiffViewModel MakeLinkedAddScenario()
    {
        var disk = DialogProject.Empty("p");
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [NodeWithLink(5, 6), Node(6)], [], []));
        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, DialogProjectSerializer.Serialize(refProject), "main\n");
        return new DiffViewModel(git, new StubDispatcher(), path);
    }

    [Fact]
    public void DiffViewModel_TickingAddedNode_AutoPullsLinkedAddedNode()
    {
        var vm = MakeLinkedAddScenario();
        var group = vm.Groups.First(g => g.Name == "greeting");

        group.Nodes.First(n => n.NodeId == 5).IsSelected = true;

        Assert.True(group.Nodes.First(n => n.NodeId == 6).IsSelected);
    }

    [Fact]
    public void DiffViewModel_AutoPullToggleOff_DisablesPull()
    {
        var vm = MakeLinkedAddScenario();
        vm.AutoPullDependencies = false;
        var group = vm.Groups.First(g => g.Name == "greeting");

        group.Nodes.First(n => n.NodeId == 5).IsSelected = true;

        Assert.False(group.Nodes.First(n => n.NodeId == 6).IsSelected);
    }
```

- [ ] **Step 2: Run, confirm fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffViewModelApplyTests"
```
Expected: compile error — `AutoPullDependencies` does not exist.

- [ ] **Step 3: Add the toggle property**

In `DiffViewModel.cs`, alongside the other `[ObservableProperty]` fields:

```csharp
    [ObservableProperty] private bool _autoPullDependencies = true;
```

- [ ] **Step 4: Build dependency data and pass it to groups in `Recompute`**

In `Recompute`, replace the group-building loop:

```csharp
        foreach (var change in results)
        {
            var group = new ConversationChangeViewModel(change);
            group.SelectionChanged += OnSelectionChanged;
            Groups.Add(group);
        }
```

with:

```csharp
        foreach (var change in results)
        {
            var group = new ConversationChangeViewModel(change);
            group.SelectionChanged += OnSelectionChanged;
            var (outgoing, addedIds) = BuildDependencyData(change);
            group.SetDependencies(outgoing, addedIds);
            group.AutoPullEnabled = AutoPullDependencies;
            Groups.Add(group);
        }
```

- [ ] **Step 5: Add the dependency-data builder and the toggle-change handler**

Add these methods to `DiffViewModel` (e.g. just below `Recompute`):

```csharp
    private (IReadOnlyDictionary<int, IReadOnlyList<int>> Outgoing, IReadOnlySet<int> AddedIds)
        BuildDependencyData(ConversationChange change)
    {
        var addedIds = (IReadOnlySet<int>)change.Added.ToHashSet();
        var outgoing = new Dictionary<int, IReadOnlyList<int>>();

        var sourcePatch = SourceProject?.Patches.GetValueOrDefault(change.Name);
        if (sourcePatch is not null)
        {
            foreach (var n in sourcePatch.AddedNodes)
                outgoing[n.NodeId] = n.Links.Select(l => l.ToNodeId).ToList();
            foreach (var m in sourcePatch.ModifiedNodes)
                outgoing[m.NodeId] = m.AddedLinks.Select(l => l.ToNodeId)
                    .Concat(m.ModifiedLinks.Select(l => l.ToNodeId)).ToList();
        }

        return (outgoing, addedIds);
    }

    partial void OnAutoPullDependenciesChanged(bool value)
    {
        foreach (var group in Groups)
            group.AutoPullEnabled = value;
    }
```

`SourceProject` is the existing private property (the non-working-copy endpoint). `ModifiedLink` exposes `ToNodeId` (used elsewhere in the patch layer). `System.Linq` is already in scope.

- [ ] **Step 6: Run, confirm pass** (`...DiffViewModelApplyTests`) — expect all pass.

- [ ] **Step 7: Commit**

```
git add DialogEditor.ViewModels/ViewModels/DiffViewModel.cs DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs
git commit -m "feat: DiffViewModel builds dependency maps and owns the auto-pull toggle"
```

---

### Task 4: The toggle checkbox in the view + strings

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml`
- Modify: `DialogEditor.Tests/Views/DiffWindowTests.cs`

- [ ] **Step 1: Add strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, near the other `Diff_*` keys (search `Diff_Hint`):

```xml
    <sys:String x:Key="Diff_AutoPullLabel">Also bring in linked nodes</sys:String>
    <sys:String x:Key="ToolTip_Diff_AutoPull">When ticking a change, also tick the added nodes it links to, so brought-in links don't point at nodes that were never created. Turn off for surgical single-node picks.</sys:String>
```

- [ ] **Step 2: Write the failing headless test**

Add to `DiffWindowTests`:

```csharp
    [AvaloniaFact]
    public void AutoPullCheckbox_DefaultsChecked_AndBindsToViewModel()
    {
        var disk = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1), Node(2)], [], []));
        var refp = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1)], [], []));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refp));
        var vm   = new DiffViewModel(git, new StubDispatcher(), path);

        var window = new DiffWindow(vm);
        window.Show();

        var cb = window.FindControl<CheckBox>("AutoPullCheck")!;
        Assert.True(cb.IsChecked);

        vm.AutoPullDependencies = false;
        Assert.False(cb.IsChecked);
    }
```

- [ ] **Step 3: Run, confirm fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffWindowTests"
```
Expected: lookup failure — there is no control named `AutoPullCheck`.

- [ ] **Step 4: Add the checkbox to the apply bar**

In `DiffWindow.axaml`, the apply bar's left `StackPanel` currently holds only the hint `TextBlock`. Add the checkbox above it:

```xml
                <StackPanel>
                    <CheckBox x:Name="AutoPullCheck"
                              Content="{StaticResource Diff_AutoPullLabel}"
                              IsChecked="{Binding AutoPullDependencies, Mode=TwoWay}"
                              Foreground="#ccc" FontSize="11"
                              ToolTip.Tip="{StaticResource ToolTip_Diff_AutoPull}"/>
                    <TextBlock Text="{StaticResource Diff_Hint}" Foreground="#888" FontSize="11" VerticalAlignment="Center"/>
                </StackPanel>
```

- [ ] **Step 5: Build**

```
dotnet build DialogEditor.Avalonia
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 6: Run the headless tests** (`...DiffWindowTests`) — expect all pass.

- [ ] **Step 7: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/Views/DiffWindow.axaml DialogEditor.Tests/Views/DiffWindowTests.cs
git commit -m "feat: 'Also bring in linked nodes' toggle in the diff window"
```

---

### Task 5: Update Gaps.md and full verification

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Update Gaps.md**

In the **Selective apply** paragraph, find:

> One follow-up remains intentionally: automatic dependency-pulling.

Replace with:

> Automatic dependency-pulling is implemented: ticking a change auto-ticks the added nodes it links to (transitively), preventing brought-in links that point at nodes never created. It is outgoing-only and has a default-on "Also bring in linked nodes" toggle.

If the exact sentence is not found, search for "dependency-pulling" and report before editing.

- [ ] **Step 2: Full verification**

```
dotnet test DialogEditor.Tests
dotnet build
```
Expected: all tests pass; `Build succeeded. 0 Error(s)`. If anything fails, stop and report — do not commit.

- [ ] **Step 3: Commit**

```
git add Gaps.md
git commit -m "docs: record automatic dependency-pulling as implemented"
```

---

## Verification Checklist

1. `dotnet test DialogEditor.Tests` — all pass (suite runs serially by design).
2. `dotnet build` — 0 errors.
3. **Manual:** open the diff window with the working copy as one endpoint; in a conversation where an added node links to another added node, tick the first — the linked added node ticks automatically.
4. **Manual:** the pull is transitive (a chain of linked additions all tick).
5. **Manual:** untick "Also bring in linked nodes", then tick a node — nothing extra is pulled.
6. **Manual:** unticking a node leaves its previously-pulled dependencies ticked.
7. **Manual:** ticking a node that links to a *modified* or *removed* node does not pull it (modified already exists; removed is the dangling panel's domain).
