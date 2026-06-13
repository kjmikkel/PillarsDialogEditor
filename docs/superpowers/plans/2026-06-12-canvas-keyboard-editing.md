# Canvas Keyboard Editing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the conversation canvas fully keyboard-operable: topological arrow traversal, full-node cycling, node nudging, context-menu access, and detail-panel handoff — with the key map documented in plain language in the Legend window.

**Architecture:** Pure traversal logic (`CanvasNavigationService`) and selection/navigation methods on `ConversationViewModel` live in `DialogEditor.ViewModels` and are unit-tested with plain xUnit. The view contributes only a dumb `KeyDown` mapping on the `NodifyEditor` plus `BringIntoView` follow-up, covered by headless `[AvaloniaFact]` tests. Spec: `docs/superpowers/specs/2026-06-12-canvas-keyboard-editing-design.md`.

**Tech Stack:** C# / .NET 8, Avalonia 11.3.14, NodifyAvalonia 6.6.0, CommunityToolkit.Mvvm, xUnit + Avalonia.Headless.XUnit.

---

## Codebase facts you need (verified 2026-06-12)

- `ConversationViewModel` (in `DialogEditor.ViewModels\ViewModels\ConversationViewModel.cs`):
  `ObservableCollection<NodeViewModel> Nodes`, `ObservableCollection<ConnectionViewModel> Connections`,
  `[ObservableProperty] NodeViewModel? _selectedNode` (so `SelectedNode` exists, with a
  `partial void OnSelectedNodeChanged(NodeViewModel? value)` already defined for connection
  highlighting), `bool IsEditable` (observable). Constructor: `new ConversationViewModel(IDispatcher)`;
  tests use `new ConversationViewModel(new StubDispatcher())` (existing helper, see
  `DialogEditor.Tests\ViewModels\ConversationStatisticsTests.cs:21`).
- `NodeViewModel`: `int NodeId`, `[ObservableProperty] LayoutPoint _location`,
  `[ObservableProperty] bool _isSelected` (setting `true` invokes `OnSelected` → `SelectedNode = n`),
  single connectors `ConnectorViewModel Input { get; }` / `Output { get; }`.
  Constructor: `new NodeViewModel(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None"), new StringEntry(id, "", ""))`.
- `ConnectorViewModel`: `internal NodeViewModel? Owner { get; set; }`, `public int GetNodeId()`.
  `DialogEditor.ViewModels` has `[InternalsVisibleTo("DialogEditor.Tests")]`, so tests may set `Owner`.
- `ConnectionViewModel`: `Source`/`Target` are `ConnectorViewModel`; `new ConnectionViewModel(parent.Output, child.Input)`.
- `LayoutPoint`: `readonly record struct LayoutPoint(double X, double Y)` (`DialogEditor.Core\Models\LayoutPoint.cs`).
- Root node = `NodeId == 0` (see `CenterOnRoot_Click` in `ConversationView.axaml.cs:47`).
- `ConversationView.axaml.cs` already has `FocusEditor()`, `ScrollToNode(node)`, and the editor is `x:Name="Editor"`.
- `MainWindow.axaml.cs` handles global keys in a **tunnel** handler (`OnKeyDownTunnel`); it does NOT
  consume bare arrows/PgUp/PgDn/Home/Enter, so the editor's bubble `KeyDown` is safe.
- Test suite runs **serially by design** (AppSettings/Loc global state) — do not parallelize.
- Project rules: strict red/green TDD; no hard-coded user-visible strings (resources only);
  `AutomationNameTests` enforce accessible names; new interactive controls need tooltips (none are added here).
- Run tests with: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~<TestClassName>"`,
  full suite with `dotnet test DialogEditor.Tests`.

---

### Task 1: CanvasNavigationService — child and parent traversal

**Files:**
- Create: `DialogEditor.ViewModels\Services\CanvasNavigationService.cs`
- Test: `DialogEditor.Tests\ViewModels\CanvasNavigationServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class CanvasNavigationServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────
    internal static NodeViewModel MakeNode(int id, double x = 0, double y = 0)
    {
        var n = new NodeViewModel(
            new ConversationNode(id, false, SpeakerCategory.Npc,
                string.Empty, string.Empty, [], [], [], "Conversation", "None"),
            new StringEntry(id, string.Empty, string.Empty))
        { Location = new LayoutPoint(x, y) };
        // Owner is internal; DialogEditor.ViewModels has InternalsVisibleTo(DialogEditor.Tests)
        n.Input.Owner  = n;
        n.Output.Owner = n;
        return n;
    }

    internal static ConnectionViewModel Connect(NodeViewModel parent, NodeViewModel child) =>
        new(parent.Output, child.Input);

    // ── Child ─────────────────────────────────────────────────────────────
    [Fact]
    public void GetChild_SingleChild_ReturnsIt()
    {
        var a = MakeNode(0); var b = MakeNode(1, 400, 0);
        var nodes = new[] { a, b };
        var conns = new[] { Connect(a, b) };
        Assert.Same(b, CanvasNavigationService.GetChild(a, nodes, conns));
    }

    [Fact]
    public void GetChild_MultipleChildren_PicksVerticallyNearest()
    {
        var a = MakeNode(0, 0, 100);
        var far  = MakeNode(1, 400, 300);
        var near = MakeNode(2, 400, 120);
        var nodes = new[] { a, far, near };
        var conns = new[] { Connect(a, far), Connect(a, near) };
        Assert.Same(near, CanvasNavigationService.GetChild(a, nodes, conns));
    }

    [Fact]
    public void GetChild_TieOnDistance_PicksFirstLinkOrder()
    {
        var a = MakeNode(0, 0, 100);
        var up   = MakeNode(1, 400, 50);   // |50-100|  = 50
        var down = MakeNode(2, 400, 150);  // |150-100| = 50 — tie
        var nodes = new[] { a, up, down };
        var conns = new[] { Connect(a, up), Connect(a, down) };
        Assert.Same(up, CanvasNavigationService.GetChild(a, nodes, conns)); // first connection wins
    }

    [Fact]
    public void GetChild_NoChildren_ReturnsNull()
    {
        var a = MakeNode(0);
        Assert.Null(CanvasNavigationService.GetChild(a, new[] { a }, []));
    }

    [Fact]
    public void GetChild_SelfLoop_IsIgnored()
    {
        var a = MakeNode(0);
        var conns = new[] { Connect(a, a) };
        Assert.Null(CanvasNavigationService.GetChild(a, new[] { a }, conns));
    }

    // ── Parent ────────────────────────────────────────────────────────────
    [Fact]
    public void GetParent_SingleParent_ReturnsIt()
    {
        var a = MakeNode(0); var b = MakeNode(1, 400, 0);
        var nodes = new[] { a, b };
        var conns = new[] { Connect(a, b) };
        Assert.Same(a, CanvasNavigationService.GetParent(b, nodes, conns));
    }

    [Fact]
    public void GetParent_MultipleParents_PicksVerticallyNearest()
    {
        var child = MakeNode(2, 400, 100);
        var far  = MakeNode(0, 0, 400);
        var near = MakeNode(1, 0, 90);
        var nodes = new[] { far, near, child };
        var conns = new[] { Connect(far, child), Connect(near, child) };
        Assert.Same(near, CanvasNavigationService.GetParent(child, nodes, conns));
    }

    [Fact]
    public void GetParent_Root_ReturnsNull()
    {
        var a = MakeNode(0); var b = MakeNode(1);
        var conns = new[] { Connect(a, b) };
        Assert.Null(CanvasNavigationService.GetParent(a, new[] { a, b }, conns));
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasNavigationServiceTests"`
Expected: compile error — `CanvasNavigationService` does not exist. (A compile error is the
correct RED here; the type is new.)

- [ ] **Step 3: Write the implementation**

```csharp
namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Pure keyboard-traversal logic for the conversation canvas (spec:
/// docs/superpowers/specs/2026-06-12-canvas-keyboard-editing-design.md).
///
/// Traversal is TOPOLOGICAL (follows links), not spatial: it matches both the
/// left-to-right auto-layout and the dialog structure writers think in, and it
/// stays correct after nodes are hand-rearranged. Where several candidates
/// exist (multiple children/parents), the one vertically nearest the current
/// node wins, so the keyboard "follows the eye"; ties keep link order (LINQ
/// OrderBy is stable). Self-loops are skipped — navigating to yourself is a
/// no-op, not a move.
/// </summary>
public static class CanvasNavigationService
{
    public static NodeViewModel? GetChild(
        NodeViewModel from,
        IReadOnlyList<NodeViewModel> nodes,
        IEnumerable<ConnectionViewModel> connections) =>
        NearestByY(from, connections
            .Where(c => c.Source.GetNodeId() == from.NodeId)
            .Select(c => ById(nodes, c.Target.GetNodeId()))
            .OfType<NodeViewModel>()
            .Where(n => n.NodeId != from.NodeId));

    public static NodeViewModel? GetParent(
        NodeViewModel from,
        IReadOnlyList<NodeViewModel> nodes,
        IEnumerable<ConnectionViewModel> connections) =>
        NearestByY(from, connections
            .Where(c => c.Target.GetNodeId() == from.NodeId)
            .Select(c => ById(nodes, c.Source.GetNodeId()))
            .OfType<NodeViewModel>()
            .Where(n => n.NodeId != from.NodeId));

    private static NodeViewModel? ById(IReadOnlyList<NodeViewModel> nodes, int id) =>
        nodes.FirstOrDefault(n => n.NodeId == id);

    private static NodeViewModel? NearestByY(NodeViewModel from, IEnumerable<NodeViewModel> candidates) =>
        candidates.OrderBy(n => Math.Abs(n.Location.Y - from.Location.Y)).FirstOrDefault();
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasNavigationServiceTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/CanvasNavigationService.cs DialogEditor.Tests/ViewModels/CanvasNavigationServiceTests.cs
git commit -m "feat(a11y): canvas keyboard traversal - child/parent (nearest-by-Y)"
```

---

### Task 2: CanvasNavigationService — siblings

**Files:**
- Modify: `DialogEditor.ViewModels\Services\CanvasNavigationService.cs`
- Test: `DialogEditor.Tests\ViewModels\CanvasNavigationServiceTests.cs` (append)

- [ ] **Step 1: Write the failing tests** (append inside the test class)

```csharp
    // ── Siblings ──────────────────────────────────────────────────────────
    [Fact]
    public void GetSibling_NextAndPrevious_InVisualOrder()
    {
        var p  = MakeNode(0, 0, 100);
        var s1 = MakeNode(1, 400, 50);
        var s2 = MakeNode(2, 400, 150);
        var s3 = MakeNode(3, 400, 250);
        var nodes = new[] { p, s1, s2, s3 };
        var conns = new[] { Connect(p, s1), Connect(p, s2), Connect(p, s3) };
        Assert.Same(s3, CanvasNavigationService.GetSibling(s2, +1, nodes, conns));
        Assert.Same(s1, CanvasNavigationService.GetSibling(s2, -1, nodes, conns));
    }

    [Fact]
    public void GetSibling_AtEnds_DoesNotWrap()
    {
        var p  = MakeNode(0, 0, 100);
        var s1 = MakeNode(1, 400, 50);
        var s2 = MakeNode(2, 400, 150);
        var nodes = new[] { p, s1, s2 };
        var conns = new[] { Connect(p, s1), Connect(p, s2) };
        Assert.Null(CanvasNavigationService.GetSibling(s1, -1, nodes, conns));
        Assert.Null(CanvasNavigationService.GetSibling(s2, +1, nodes, conns));
    }

    [Fact]
    public void GetSibling_ParentlessNodes_FormOneGroup()
    {
        // Roots and orphans are each other's siblings, ordered by Y.
        var root   = MakeNode(0, 0, 0);
        var orphan = MakeNode(5, 800, 200);
        var child  = MakeNode(1, 400, 0);
        var nodes = new[] { root, orphan, child };
        var conns = new[] { Connect(root, child) };
        Assert.Same(orphan, CanvasNavigationService.GetSibling(root, +1, nodes, conns));
        Assert.Same(root,   CanvasNavigationService.GetSibling(orphan, -1, nodes, conns));
    }

    [Fact]
    public void GetSibling_OnlyChild_ReturnsNull()
    {
        var p = MakeNode(0); var c = MakeNode(1, 400, 0);
        var nodes = new[] { p, c };
        var conns = new[] { Connect(p, c) };
        Assert.Null(CanvasNavigationService.GetSibling(c, +1, nodes, conns));
        Assert.Null(CanvasNavigationService.GetSibling(c, -1, nodes, conns));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasNavigationServiceTests"`
Expected: compile error — `GetSibling` not defined.

- [ ] **Step 3: Implement** (append to `CanvasNavigationService`)

```csharp
    /// <summary>
    /// Siblings of a node = children of its primary parent (the same parent ←
    /// navigates to), in visual (Y) order, no wrap. Parentless nodes (roots and
    /// orphans) form a single sibling group so ↑/↓ can hop between disconnected
    /// islands without the mouse.
    /// </summary>
    public static NodeViewModel? GetSibling(
        NodeViewModel from,
        int offset,
        IReadOnlyList<NodeViewModel> nodes,
        IEnumerable<ConnectionViewModel> connections)
    {
        var connList = connections as IReadOnlyList<ConnectionViewModel> ?? connections.ToList();
        var parent = GetParent(from, nodes, connList);

        var group = (parent is null
                ? nodes.Where(n => GetParent(n, nodes, connList) is null)
                : connList.Where(c => c.Source.GetNodeId() == parent.NodeId)
                          .Select(c => ById(nodes, c.Target.GetNodeId()))
                          .OfType<NodeViewModel>()
                          .Distinct())
            .OrderBy(n => n.Location.Y)
            .ToList();

        var index = group.IndexOf(from);
        if (index < 0) return null;
        var target = index + offset;
        return target >= 0 && target < group.Count ? group[target] : null;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasNavigationServiceTests"`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/CanvasNavigationService.cs DialogEditor.Tests/ViewModels/CanvasNavigationServiceTests.cs
git commit -m "feat(a11y): canvas keyboard traversal - siblings (Y-order, parentless group)"
```

---

### Task 3: CanvasNavigationService — full-node cycle

**Files:**
- Modify: `DialogEditor.ViewModels\Services\CanvasNavigationService.cs`
- Test: `DialogEditor.Tests\ViewModels\CanvasNavigationServiceTests.cs` (append)

- [ ] **Step 1: Write the failing tests** (append inside the test class)

```csharp
    // ── Cycle ─────────────────────────────────────────────────────────────
    [Fact]
    public void Cycle_Forward_FollowsCollectionOrderAndWraps()
    {
        var a = MakeNode(0); var b = MakeNode(1); var c = MakeNode(2);
        var nodes = new[] { a, b, c };
        Assert.Same(b, CanvasNavigationService.Cycle(a, forward: true, nodes));
        Assert.Same(a, CanvasNavigationService.Cycle(c, forward: true, nodes)); // wraps
    }

    [Fact]
    public void Cycle_Backward_Wraps()
    {
        var a = MakeNode(0); var b = MakeNode(1);
        var nodes = new[] { a, b };
        Assert.Same(b, CanvasNavigationService.Cycle(a, forward: false, nodes)); // wraps
    }

    [Fact]
    public void Cycle_FromNull_EntersAtFirstOrLast()
    {
        var a = MakeNode(0); var b = MakeNode(1);
        var nodes = new[] { a, b };
        Assert.Same(a, CanvasNavigationService.Cycle(null, forward: true, nodes));
        Assert.Same(b, CanvasNavigationService.Cycle(null, forward: false, nodes));
    }

    [Fact]
    public void Cycle_ReachesOrphans()
    {
        var root = MakeNode(0); var orphan = MakeNode(7);
        var nodes = new[] { root, orphan };
        Assert.Same(orphan, CanvasNavigationService.Cycle(root, forward: true, nodes));
    }

    [Fact]
    public void Cycle_EmptyCanvas_ReturnsNull()
    {
        Assert.Null(CanvasNavigationService.Cycle(null, forward: true, []));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasNavigationServiceTests"`
Expected: compile error — `Cycle` not defined.

- [ ] **Step 3: Implement** (append to `CanvasNavigationService`)

```csharp
    /// <summary>
    /// PgUp/PgDn step through every node in stable collection (file) order,
    /// wrapping. This is the keyboard-coverage guarantee: orphans with no
    /// connections are unreachable by arrow traversal but always reachable here.
    /// </summary>
    public static NodeViewModel? Cycle(
        NodeViewModel? from,
        bool forward,
        IReadOnlyList<NodeViewModel> nodes)
    {
        if (nodes.Count == 0) return null;
        if (from is null) return forward ? nodes[0] : nodes[^1];

        var index = 0;
        for (var i = 0; i < nodes.Count; i++)
            if (ReferenceEquals(nodes[i], from)) { index = i; break; }

        var target = (index + (forward ? 1 : -1) + nodes.Count) % nodes.Count;
        return nodes[target];
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasNavigationServiceTests"`
Expected: PASS (17 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/CanvasNavigationService.cs DialogEditor.Tests/ViewModels/CanvasNavigationServiceTests.cs
git commit -m "feat(a11y): canvas keyboard traversal - PgUp/PgDn full-node cycle"
```

---

### Task 4: ConversationViewModel — selection plumbing

**Files:**
- Modify: `DialogEditor.ViewModels\ViewModels\ConversationViewModel.cs`
- Test: `DialogEditor.Tests\ViewModels\CanvasKeyboardViewModelTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.ViewModels;

public class CanvasKeyboardViewModelTests
{
    private static ConversationViewModel MakeVm() => new(new StubDispatcher());

    private static NodeViewModel AddNode(ConversationViewModel vm, int id, double x = 0, double y = 0)
    {
        var n = CanvasNavigationServiceTests.MakeNode(id, x, y);
        vm.Nodes.Add(n);
        n.OnSelected = node => vm.SelectedNode = node; // same wiring Load() applies
        return n;
    }

    // ── SelectNode ────────────────────────────────────────────────────────
    [Fact]
    public void SelectNode_SetsSelectionAndClearsOthers()
    {
        var vm = MakeVm();
        var a = AddNode(vm, 0); var b = AddNode(vm, 1);
        a.IsSelected = true;

        vm.SelectNode(b);

        Assert.True(b.IsSelected);
        Assert.False(a.IsSelected);
        Assert.Same(b, vm.SelectedNode);
    }

    // ── Deselect ──────────────────────────────────────────────────────────
    [Fact]
    public void Deselect_ClearsSelection()
    {
        var vm = MakeVm();
        var a = AddNode(vm, 0);
        vm.SelectNode(a);

        Assert.True(vm.Deselect());
        Assert.Null(vm.SelectedNode);
        Assert.False(a.IsSelected);
        Assert.False(vm.Deselect()); // nothing selected → false
    }

    // ── EnsureKeyboardSelection ───────────────────────────────────────────
    [Fact]
    public void EnsureKeyboardSelection_RestoresLastSelection()
    {
        var vm = MakeVm();
        var a = AddNode(vm, 0); var b = AddNode(vm, 1);
        vm.SelectNode(b);
        vm.Deselect();

        Assert.True(vm.EnsureKeyboardSelection());
        Assert.Same(b, vm.SelectedNode);
    }

    [Fact]
    public void EnsureKeyboardSelection_FirstFocus_SelectsRoot()
    {
        var vm = MakeVm();
        var other = AddNode(vm, 3);
        var root  = AddNode(vm, 0);

        Assert.True(vm.EnsureKeyboardSelection());
        Assert.Same(root, vm.SelectedNode); // NodeId == 0, not collection order
    }

    [Fact]
    public void EnsureKeyboardSelection_AlreadySelected_KeepsIt()
    {
        var vm = MakeVm();
        var a = AddNode(vm, 0); var b = AddNode(vm, 1);
        vm.SelectNode(b);

        Assert.True(vm.EnsureKeyboardSelection());
        Assert.Same(b, vm.SelectedNode);
    }

    [Fact]
    public void EnsureKeyboardSelection_LastSelectionDeleted_FallsBackToRoot()
    {
        var vm = MakeVm();
        var root = AddNode(vm, 0); var b = AddNode(vm, 1);
        vm.SelectNode(b);
        vm.Deselect();
        vm.Nodes.Remove(b);

        Assert.True(vm.EnsureKeyboardSelection());
        Assert.Same(root, vm.SelectedNode);
    }

    [Fact]
    public void EnsureKeyboardSelection_EmptyCanvas_ReturnsFalse()
    {
        Assert.False(MakeVm().EnsureKeyboardSelection());
    }

    // ── TrySelectRoot ─────────────────────────────────────────────────────
    [Fact]
    public void TrySelectRoot_SelectsNodeIdZero_FallsBackToFirst()
    {
        var vm = MakeVm();
        var other = AddNode(vm, 3);
        var root  = AddNode(vm, 0);
        Assert.True(vm.TrySelectRoot());
        Assert.Same(root, vm.SelectedNode);

        var vm2 = MakeVm();
        var only = AddNode(vm2, 9);
        Assert.True(vm2.TrySelectRoot());
        Assert.Same(only, vm2.SelectedNode);

        Assert.False(MakeVm().TrySelectRoot());
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasKeyboardViewModelTests"`
Expected: compile error — `SelectNode`/`Deselect`/`EnsureKeyboardSelection`/`TrySelectRoot` not defined.

- [ ] **Step 3: Implement** (in `ConversationViewModel.cs`)

3a. Extend the existing `OnSelectedNodeChanged` partial (around line 165) — add the
last-selection memory as its FIRST line (keep the highlighting loop unchanged):

```csharp
    partial void OnSelectedNodeChanged(NodeViewModel? value)
    {
        // Remember the last real selection (mouse or keyboard) so keyboard focus
        // can resume where the user left off (spec: entry = restore last selection).
        if (value is not null) _lastSelection = value;

        foreach (var connection in Connections)
        {
            connection.IsHighlighted = value is not null &&
                (connection.Source == value.Output || connection.Target == value.Input);
        }
    }
```

3b. Add a new region after the structural-edit methods (e.g. after `AddConnectedNode`,
around line 355):

```csharp
    // ── Keyboard selection & navigation (spec: 2026-06-12 canvas keyboard) ──
    private NodeViewModel? _lastSelection;

    /// Single selection path for mouse AND keyboard: clears other nodes'
    /// IsSelected (Nodify renders the selection ring from it) and sets
    /// SelectedNode (drives connection highlighting + the detail panel).
    public void SelectNode(NodeViewModel node)
    {
        foreach (var n in Nodes)
            if (!ReferenceEquals(n, node) && n.IsSelected)
                n.IsSelected = false;
        node.IsSelected = true; // OnSelected callback also sets SelectedNode
        SelectedNode = node;    // ...but set explicitly for nodes lacking the callback
    }

    public bool Deselect()
    {
        if (SelectedNode is null) return false;
        SelectedNode.IsSelected = false;
        SelectedNode = null;
        return true;
    }

    /// Keyboard focus arriving on an empty selection resumes at the last
    /// selection; first focus (or a deleted last selection) starts at the root.
    public bool EnsureKeyboardSelection()
    {
        if (SelectedNode is not null) return true;
        if (Nodes.Count == 0) return false;

        var target = _lastSelection is not null && Nodes.Contains(_lastSelection)
            ? _lastSelection
            : Nodes.FirstOrDefault(n => n.NodeId == 0) ?? Nodes[0];
        SelectNode(target);
        return true;
    }

    public bool TrySelectRoot()
    {
        var root = Nodes.FirstOrDefault(n => n.NodeId == 0) ?? Nodes.FirstOrDefault();
        if (root is null) return false;
        SelectNode(root);
        return true;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasKeyboardViewModelTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs DialogEditor.Tests/ViewModels/CanvasKeyboardViewModelTests.cs
git commit -m "feat(a11y): canvas selection plumbing for keyboard (SelectNode/Ensure/Root)"
```

---

### Task 5: ConversationViewModel — TryNavigate, TryCycle, NudgeSelected

**Files:**
- Modify: `DialogEditor.ViewModels\ViewModels\ConversationViewModel.cs`
- Create: `DialogEditor.ViewModels\Services\CanvasNavDirection.cs`
- Test: `DialogEditor.Tests\ViewModels\CanvasKeyboardViewModelTests.cs` (append)

- [ ] **Step 1: Write the failing tests** (append inside the test class)

```csharp
    private static (ConversationViewModel vm, NodeViewModel root, NodeViewModel child) MakeChain()
    {
        var vm = MakeVm();
        var root  = AddNode(vm, 0, 0, 0);
        var child = AddNode(vm, 1, 400, 0);
        vm.Connections.Add(CanvasNavigationServiceTests.Connect(root, child));
        return (vm, root, child);
    }

    // ── TryNavigate ───────────────────────────────────────────────────────
    [Fact]
    public void TryNavigate_Child_MovesSelection()
    {
        var (vm, root, child) = MakeChain();
        vm.SelectNode(root);

        Assert.True(vm.TryNavigate(CanvasNavDirection.Child));
        Assert.Same(child, vm.SelectedNode);
        Assert.True(child.IsSelected);
        Assert.False(root.IsSelected);
    }

    [Fact]
    public void TryNavigate_NoCandidate_ReturnsFalseAndKeepsSelection()
    {
        var (vm, root, _) = MakeChain();
        vm.SelectNode(root);

        Assert.False(vm.TryNavigate(CanvasNavDirection.Parent)); // root has no parent
        Assert.Same(root, vm.SelectedNode);
    }

    [Fact]
    public void TryNavigate_NothingSelected_ReturnsFalse()
    {
        var (vm, _, _) = MakeChain();
        Assert.False(vm.TryNavigate(CanvasNavDirection.Child));
    }

    // ── TryCycle ──────────────────────────────────────────────────────────
    [Fact]
    public void TryCycle_MovesToNextNode_EvenFromNoSelection()
    {
        var (vm, root, child) = MakeChain();

        Assert.True(vm.TryCycle(forward: true));   // no selection → first node
        Assert.Same(root, vm.SelectedNode);
        Assert.True(vm.TryCycle(forward: true));
        Assert.Same(child, vm.SelectedNode);
        Assert.True(vm.TryCycle(forward: true));   // wraps
        Assert.Same(root, vm.SelectedNode);
    }

    [Fact]
    public void TryCycle_EmptyCanvas_ReturnsFalse()
    {
        Assert.False(MakeVm().TryCycle(forward: true));
    }

    // ── NudgeSelected ─────────────────────────────────────────────────────
    [Fact]
    public void NudgeSelected_MovesLocation()
    {
        var (vm, root, _) = MakeChain();
        vm.IsEditable = true;
        vm.SelectNode(root);

        Assert.True(vm.NudgeSelected(10, 0));
        Assert.Equal(new LayoutPoint(10, 0), root.Location);
        Assert.True(vm.NudgeSelected(0, -50));
        Assert.Equal(new LayoutPoint(10, -50), root.Location);
    }

    [Fact]
    public void NudgeSelected_NotEditable_ReturnsFalse()
    {
        var (vm, root, _) = MakeChain();
        vm.IsEditable = false; // read-only canvas (e.g. diff view) must not move nodes
        vm.SelectNode(root);

        Assert.False(vm.NudgeSelected(10, 0));
        Assert.Equal(new LayoutPoint(0, 0), root.Location);
    }

    [Fact]
    public void NudgeSelected_NothingSelected_ReturnsFalse()
    {
        var (vm, _, _) = MakeChain();
        vm.IsEditable = true;
        Assert.False(vm.NudgeSelected(10, 0));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasKeyboardViewModelTests"`
Expected: compile error — `CanvasNavDirection`/`TryNavigate`/`TryCycle`/`NudgeSelected` not defined.

- [ ] **Step 3: Implement**

3a. Create `DialogEditor.ViewModels\Services\CanvasNavDirection.cs`:

```csharp
namespace DialogEditor.ViewModels.Services;

/// Keyboard traversal directions on the conversation canvas. Topological, not
/// spatial: Child follows a link forward, Parent goes back, siblings are the
/// children of the same primary parent (see CanvasNavigationService).
public enum CanvasNavDirection
{
    Parent,
    Child,
    PreviousSibling,
    NextSibling,
}
```

3b. Append to the keyboard region in `ConversationViewModel.cs` (after `TrySelectRoot`;
add `using DialogEditor.ViewModels.Services;` is already present in the file's usings —
verify, it imports `DialogEditor.ViewModels.Services` at line 10):

```csharp
    public bool TryNavigate(CanvasNavDirection direction)
    {
        if (SelectedNode is null) return false;

        var target = direction switch
        {
            CanvasNavDirection.Parent          => CanvasNavigationService.GetParent(SelectedNode, Nodes, Connections),
            CanvasNavDirection.Child           => CanvasNavigationService.GetChild(SelectedNode, Nodes, Connections),
            CanvasNavDirection.PreviousSibling => CanvasNavigationService.GetSibling(SelectedNode, -1, Nodes, Connections),
            CanvasNavDirection.NextSibling     => CanvasNavigationService.GetSibling(SelectedNode, +1, Nodes, Connections),
            _                                  => null,
        };
        if (target is null) return false;
        SelectNode(target);
        return true;
    }

    public bool TryCycle(bool forward)
    {
        var target = CanvasNavigationService.Cycle(SelectedNode, forward, Nodes);
        if (target is null) return false;
        SelectNode(target);
        return true;
    }

    /// Keyboard nudge has drag-move semantics: a plain Location set, no undo
    /// entry (drag moves are not individually undoable today; layout persists
    /// via GetCurrentLayout at save). Gated on IsEditable so read-only canvases
    /// (diff view) cannot be rearranged from the keyboard.
    public bool NudgeSelected(double dx, double dy)
    {
        if (!IsEditable || SelectedNode is null) return false;
        SelectedNode.Location = new LayoutPoint(SelectedNode.Location.X + dx, SelectedNode.Location.Y + dy);
        return true;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasKeyboardViewModelTests"`
Expected: PASS (16 tests).

- [ ] **Step 5: Run the FULL suite** (the VM changed; prove no regression)

Run: `dotnet test DialogEditor.Tests`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels DialogEditor.Tests/ViewModels/CanvasKeyboardViewModelTests.cs
git commit -m "feat(a11y): keyboard navigate/cycle/nudge on ConversationViewModel"
```

---

### Task 6: View glue — KeyDown mapping + focus entry

**Files:**
- Modify: `DialogEditor.Avalonia\Views\ConversationView.axaml` (editor element, line ~91-96)
- Modify: `DialogEditor.Avalonia\Views\ConversationView.axaml.cs`
- Test: `DialogEditor.Tests\Views\ConversationViewKeyboardTests.cs` (create)

- [ ] **Step 1: Write the failing tests**

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.Tests.ViewModels;
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.Views;

public class ConversationViewKeyboardTests
{
    private static (Window window, ConversationView view, ConversationViewModel vm,
                    NodeViewModel root, NodeViewModel child) Setup()
    {
        var vm = new ConversationViewModel(new StubDispatcher()) { IsEditable = true };
        var root  = CanvasNavigationServiceTests.MakeNode(0, 0, 0);
        var child = CanvasNavigationServiceTests.MakeNode(1, 400, 0);
        root.OnSelected  = n => vm.SelectedNode = n;
        child.OnSelected = n => vm.SelectedNode = n;
        vm.Nodes.Add(root);
        vm.Nodes.Add(child);
        vm.Connections.Add(CanvasNavigationServiceTests.Connect(root, child));

        var view = new ConversationView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        return (window, view, vm, root, child);
    }

    private static void Press(ConversationView view, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var editor = view.FindControl<Control>("Editor")!;
        editor.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });
    }

    [AvaloniaFact]
    public void ArrowRight_SelectsChild()
    {
        var (_, view, vm, root, child) = Setup();
        vm.SelectNode(root);
        Press(view, Key.Right);
        Assert.Same(child, vm.SelectedNode);
    }

    [AvaloniaFact]
    public void ArrowLeft_SelectsParent()
    {
        var (_, view, vm, root, child) = Setup();
        vm.SelectNode(child);
        Press(view, Key.Left);
        Assert.Same(root, vm.SelectedNode);
    }

    [AvaloniaFact]
    public void CtrlArrow_NudgesSmallStep_CtrlShiftArrow_NudgesLargeStep()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);
        Press(view, Key.Right, KeyModifiers.Control);
        Assert.Equal(new LayoutPoint(10, 0), root.Location);
        Press(view, Key.Down, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal(new LayoutPoint(10, 50), root.Location);
    }

    [AvaloniaFact]
    public void PageDown_CyclesAllNodes()
    {
        var (_, view, vm, root, child) = Setup();
        Press(view, Key.PageDown);
        Assert.Same(root, vm.SelectedNode);
        Press(view, Key.PageDown);
        Assert.Same(child, vm.SelectedNode);
    }

    [AvaloniaFact]
    public void Home_SelectsRoot()
    {
        var (_, view, vm, root, child) = Setup();
        vm.SelectNode(child);
        Press(view, Key.Home);
        Assert.Same(root, vm.SelectedNode);
    }

    [AvaloniaFact]
    public void Escape_Deselects()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);
        Press(view, Key.Escape);
        Assert.Null(vm.SelectedNode);
    }

    [AvaloniaFact]
    public void Enter_RaisesFocusDetailRequested()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);
        var raised = false;
        view.FocusDetailRequested += (_, _) => raised = true;
        Press(view, Key.Enter);
        Assert.True(raised);
    }

    [AvaloniaFact]
    public void Enter_WithoutSelection_DoesNotRaise()
    {
        var (_, view, vm, _, _) = Setup();
        var raised = false;
        view.FocusDetailRequested += (_, _) => raised = true;
        Press(view, Key.Enter);
        Assert.False(raised);
    }

    [AvaloniaFact]
    public void TypingInSearchBox_DoesNotNavigate()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);
        // Raise the key on the SearchBox, not the editor: the editor handler
        // must not be attached anywhere that catches toolbar input.
        var searchBox = view.FindControl<TextBox>("SearchBox")!;
        searchBox.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Right,
        });
        Assert.Same(root, vm.SelectedNode); // unchanged
    }

    [AvaloniaFact]
    public void TabFocus_RestoresSelection()
    {
        var (_, view, vm, root, child) = Setup();
        vm.SelectNode(child);
        vm.Deselect();

        var editor = view.FindControl<Control>("Editor")!;
        editor.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });
        Assert.Same(child, vm.SelectedNode);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewKeyboardTests"`
Expected: compile error — `view.FocusDetailRequested` does not exist. Fix nothing else yet:
after adding ONLY the event stub you would see runtime failures (selection unchanged) —
either failure mode is a valid RED.

- [ ] **Step 3: Implement**

3a. In `ConversationView.axaml`, add the two event attributes to the editor element
(currently `<nodify:NodifyEditor x:Name="Editor" ... DoubleTapped="Editor_DoubleTapped">`):

```xml
            <nodify:NodifyEditor x:Name="Editor"
                                 Background="{DynamicResource Brush.Accent.Badge}"
                                 ItemsSource="{Binding Nodes}"
                                 Connections="{Binding Connections}"
                                 PendingConnection="{Binding PendingConnection}"
                                 DoubleTapped="Editor_DoubleTapped"
                                 KeyDown="Editor_KeyDown"
                                 GotFocus="Editor_GotFocus">
```

3b. In `ConversationView.axaml.cs`, add (new members on the class; add
`using DialogEditor.ViewModels.Services;` to the usings):

```csharp
    /// Raised when the user presses Enter on a selected node — MainWindow owns
    /// the detail panel and moves focus there (keyboard path into text editing).
    public event EventHandler? FocusDetailRequested;

    // Keyboard nudge steps (canvas units). Ctrl+arrow = fine, Ctrl+Shift+arrow = coarse.
    private const double NudgeStep      = 10;
    private const double NudgeStepLarge = 50;

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;

        var ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var none  = e.KeyModifiers == KeyModifiers.None;
        var step  = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? NudgeStepLarge : NudgeStep;

        var handled = e.Key switch
        {
            Key.Right when ctrl => vm.NudgeSelected(step, 0),
            Key.Left  when ctrl => vm.NudgeSelected(-step, 0),
            Key.Up    when ctrl => vm.NudgeSelected(0, -step),
            Key.Down  when ctrl => vm.NudgeSelected(0, step),

            Key.Right when none => vm.TryNavigate(CanvasNavDirection.Child),
            Key.Left  when none => vm.TryNavigate(CanvasNavDirection.Parent),
            Key.Up    when none => vm.TryNavigate(CanvasNavDirection.PreviousSibling),
            Key.Down  when none => vm.TryNavigate(CanvasNavDirection.NextSibling),

            Key.PageDown when none => vm.TryCycle(forward: true),
            Key.PageUp   when none => vm.TryCycle(forward: false),
            Key.Home     when none => vm.TrySelectRoot(),

            Key.Enter when none && vm.SelectedNode is not null => RaiseFocusDetail(),
            Key.Apps                                            => OpenSelectedNodeContextMenu(vm),
            Key.F10 when e.KeyModifiers == KeyModifiers.Shift   => OpenSelectedNodeContextMenu(vm),

            Key.Escape when none => vm.Deselect(),

            _ => false,
        };

        if (!handled) return;
        FollowSelection(vm);
        e.Handled = true;
    }

    private bool RaiseFocusDetail()
    {
        FocusDetailRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    // Keep the selected node on screen after every keyboard move.
    private void FollowSelection(ConversationViewModel vm)
    {
        if (vm.SelectedNode is { } node)
            Editor.BringIntoView(new global::Avalonia.Point(node.Location.X, node.Location.Y));
    }

    private void Editor_GotFocus(object? sender, GotFocusEventArgs e)
    {
        // Only keyboard-driven focus (Tab) auto-restores a selection. Pointer
        // focus must not: clicking empty canvas is how mouse users deselect.
        if (e.NavigationMethod != NavigationMethod.Tab) return;
        if (DataContext is not ConversationViewModel vm) return;
        if (vm.EnsureKeyboardSelection())
            FollowSelection(vm);
    }

    private bool OpenSelectedNodeContextMenu(ConversationViewModel vm)
    {
        // Placeholder until Task 7 — keep compile green; Apps/Shift+F10 no-op for now.
        return false;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewKeyboardTests"`
Expected: PASS (10 tests).

**If arrow keys do NOT reach the handler** (Nodify consuming them first — the spec's known
risk): replace the XAML `KeyDown="Editor_KeyDown"` attribute with a tunnel subscription in
the constructor and remove the attribute:

```csharp
    public ConversationView()
    {
        InitializeComponent();
        Editor.AddHandler(KeyDownEvent, Editor_KeyDown, RoutingStrategies.Tunnel);
    }
```
(`using Avalonia.Interactivity;` is already imported.) Re-run the tests.

- [ ] **Step 5: Run the FULL suite**

Run: `dotnet test DialogEditor.Tests`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConversationView.axaml DialogEditor.Avalonia/Views/ConversationView.axaml.cs DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs
git commit -m "feat(a11y): keyboard navigation on the canvas (arrows/cycle/nudge/home)"
```

---

### Task 7: Enter → detail panel focus (MainWindow wiring)

**Files:**
- Modify: `DialogEditor.Avalonia\Views\NodeDetailView.axaml` (first TextBox, line ~77)
- Modify: `DialogEditor.Avalonia\Views\NodeDetailView.axaml.cs`
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml` (NodeDetailView element, line ~268)
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml.cs`
- Test: `DialogEditor.Tests\Views\ConversationViewKeyboardTests.cs` (append)

- [ ] **Step 1: Write the failing test** (append to `ConversationViewKeyboardTests`)

```csharp
    [AvaloniaFact]
    public void NodeDetailView_FocusFirstField_FocusesDefaultTextBox()
    {
        var detail = new NodeDetailView();
        var window = new Window { Content = detail };
        window.Show();

        detail.FocusFirstField();

        var box = detail.FindControl<TextBox>("DefaultTextBox")!;
        Assert.True(box.IsFocused);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewKeyboardTests"`
Expected: compile error — `FocusFirstField` does not exist.

- [ ] **Step 3: Implement**

3a. `NodeDetailView.axaml` — name the Default/Male text box (first editable field):

```xml
                <TextBox Classes="detail-field"
                         x:Name="DefaultTextBox"
                         Text="{Binding DefaultText, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         AcceptsReturn="True" TextWrapping="Wrap" MinHeight="52"
                         ToolTip.Tip="{StaticResource ToolTip_DefaultText}"
                         AutomationProperties.Name="{StaticResource Label_DefaultMaleText}"/>
```
(Keep the existing `AutomationProperties.Name` attribute exactly as it is in the file —
only ADD `x:Name="DefaultTextBox"`.)

3b. `NodeDetailView.axaml.cs` — add:

```csharp
    /// Keyboard handoff target: Enter on a canvas node lands here (spec:
    /// 2026-06-12 canvas keyboard editing).
    public void FocusFirstField() => DefaultTextBox.Focus();
```

3c. `MainWindow.axaml` — add `x:Name="DetailView"` to the detail view element:

```xml
                    <views:NodeDetailView x:Name="DetailView" Grid.Row="1" DataContext="{Binding Detail}"
                                         IsEnabled="{Binding IsProjectOpen}"/>
```
(Note: `IsEnabled` binds `IsProjectOpen` from the WINDOW DataContext — leave as is.)

3d. `MainWindow.axaml.cs` — in the constructor, after `InitializeComponent()` (find the
existing constructor; it already wires other events), add:

```csharp
        CanvasView.FocusDetailRequested += (_, _) =>
        {
            var vm = (MainWindowViewModel)DataContext!;
            vm.IsDetailExpanded = true;        // panel may be collapsed — open it first
            DetailView.FocusFirstField();
        };
```
(`IsDetailExpanded` exists on `MainWindowViewModel` — it backs the panel's
expand/collapse toggle in MainWindow.axaml.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewKeyboardTests"`
Expected: PASS (11 tests).

- [ ] **Step 5: Run the FULL suite**

Run: `dotnet test DialogEditor.Tests`
Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Views/NodeDetailView.axaml DialogEditor.Avalonia/Views/NodeDetailView.axaml.cs DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs
git commit -m "feat(a11y): Enter on canvas node focuses the detail panel"
```

---

### Task 8: Menu key opens the node context menu

**Files:**
- Modify: `DialogEditor.Avalonia\Views\ConversationView.axaml.cs` (replace the Task 6 placeholder)
- Test: `DialogEditor.Tests\Views\ConversationViewKeyboardTests.cs` (append)

- [ ] **Step 1: Write the failing test** (append to `ConversationViewKeyboardTests`)

```csharp
    [AvaloniaFact]
    public void MenuKey_OpensSelectedNodeContextMenu()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);

        Press(view, Key.Apps);

        // The node template's ContextMenu (Delete node / Add connected node)
        // must be open. Find it via the realized container.
        var editor = view.FindControl<Control>("Editor")!;
        var menu = ((Avalonia.Visual)editor).GetVisualDescendants()
            .OfType<Control>()
            .Select(c => c.ContextMenu)
            .FirstOrDefault(m => m is not null);
        Assert.NotNull(menu);
        Assert.True(menu!.IsOpen);
    }
```
Add `using Avalonia.VisualTree;` to the test file's usings.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewKeyboardTests"`
Expected: FAIL — `Assert.True(menu.IsOpen)` is false (placeholder returns false, menu never opened).
If `Assert.NotNull(menu)` fails instead, the container template was not realized in the
headless run — insert `Avalonia.Threading.Dispatcher.UIThread.RunJobs();` after
`window.Show()` in `Setup()` and re-run before concluding.

- [ ] **Step 3: Implement** — replace the Task 6 placeholder in `ConversationView.axaml.cs`
(add `using Avalonia.VisualTree;`):

```csharp
    private bool OpenSelectedNodeContextMenu(ConversationViewModel vm)
    {
        if (vm.SelectedNode is null) return false;

        // The ContextMenu lives on the nodify:Node inside the item template, not
        // on the ItemContainer itself — walk down from the realized container.
        var container = Editor.ContainerFromItem(vm.SelectedNode);
        var owner = container?.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.ContextMenu is not null);
        if (owner?.ContextMenu is not { } menu) return false;

        menu.Open(owner);
        return true;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewKeyboardTests"`
Expected: PASS (12 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConversationView.axaml.cs DialogEditor.Tests/Views/ConversationViewKeyboardTests.cs
git commit -m "feat(a11y): Menu key / Shift+F10 opens the canvas node context menu"
```

---

### Task 9: Legend documentation (plain language, localized)

**Files:**
- Modify: `DialogEditor.Avalonia\Resources\Strings.axaml`
- Modify: `DialogEditor.Avalonia\Views\LegendWindow.axaml`

No behavioural test (static markup + resources; `AutomationNameTests` and the build cover
structure). This is the user-mandated in-app documentation — do not skip it.

- [ ] **Step 1: Add the strings** to `DialogEditor.Avalonia\Resources\Strings.axaml`
(append near the other `Legend_*` strings; keep the file's comment style):

```xml
    <!-- ─── Legend: canvas keyboard (plain language for non-technical writers) ── -->
    <sys:String x:Key="Legend_CanvasKeys">Canvas keyboard</sys:String>
    <sys:String x:Key="Legend_Key_Right">→</sys:String>
    <sys:String x:Key="Legend_Key_Right_Desc">Move to the line that follows this one</sys:String>
    <sys:String x:Key="Legend_Key_Left">←</sys:String>
    <sys:String x:Key="Legend_Key_Left_Desc">Go back to the line this one responds to</sys:String>
    <sys:String x:Key="Legend_Key_UpDown">↑ / ↓</sys:String>
    <sys:String x:Key="Legend_Key_UpDown_Desc">Switch between sibling responses</sys:String>
    <sys:String x:Key="Legend_Key_PageUpDown">Page Up / Down</sys:String>
    <sys:String x:Key="Legend_Key_PageUpDown_Desc">Step through every node, including unconnected ones</sys:String>
    <sys:String x:Key="Legend_Key_Home">Home</sys:String>
    <sys:String x:Key="Legend_Key_Home_Desc">Jump back to the start of the conversation</sys:String>
    <sys:String x:Key="Legend_Key_CtrlArrows">Ctrl + arrows</sys:String>
    <sys:String x:Key="Legend_Key_CtrlArrows_Desc">Move the selected node a small step (add Shift for bigger steps)</sys:String>
    <sys:String x:Key="Legend_Key_Enter">Enter</sys:String>
    <sys:String x:Key="Legend_Key_Enter_Desc">Edit the selected node's text and details</sys:String>
    <sys:String x:Key="Legend_Key_Menu">Menu key</sys:String>
    <sys:String x:Key="Legend_Key_Menu_Desc">Open the selected node's right-click menu</sys:String>
    <sys:String x:Key="Legend_Key_Escape">Esc</sys:String>
    <sys:String x:Key="Legend_Key_Escape_Desc">Deselect the current node</sys:String>
```

(Note: this file uses `sys:String` — match whichever prefix the file actually declares;
`DialogEditor.Avalonia\Resources\Strings.axaml` maps `xmlns:sys="clr-namespace:System;assembly=System.Runtime"`.)

- [ ] **Step 2: Add the rows** to `LegendWindow.axaml`, after the LAST existing shortcut
row (the `Legend_Control_Escape` Grid that closes the shortcuts section, line ~160-163),
inside the same StackPanel:

```xml
            <TextBlock Text="{StaticResource Legend_CanvasKeys}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="10" FontWeight="Bold" Margin="0,8,0,6"/>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_Right}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_Right_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" TextWrapping="Wrap"/>
            </Grid>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_Left}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_Left_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" TextWrapping="Wrap"/>
            </Grid>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_UpDown}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_UpDown_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" TextWrapping="Wrap"/>
            </Grid>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_PageUpDown}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_PageUpDown_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" TextWrapping="Wrap"/>
            </Grid>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_Home}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_Home_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" TextWrapping="Wrap"/>
            </Grid>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_CtrlArrows}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_CtrlArrows_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" TextWrapping="Wrap"/>
            </Grid>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_Enter}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_Enter_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" TextWrapping="Wrap"/>
            </Grid>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_Menu}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_Menu_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" TextWrapping="Wrap"/>
            </Grid>
            <Grid ColumnDefinitions="110,*" Margin="0,0,0,3">
                <TextBlock Grid.Column="0" Text="{StaticResource Legend_Key_Escape}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                <TextBlock Grid.Column="1" Text="{StaticResource Legend_Key_Escape_Desc}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" TextWrapping="Wrap"/>
            </Grid>
```

- [ ] **Step 3: Build + full suite** (resource keys are resolved at runtime; the suite's
headless app instantiation catches missing keys)

Run: `dotnet test DialogEditor.Tests`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/Views/LegendWindow.axaml
git commit -m "docs(a11y): document canvas keyboard controls in the Legend window"
```

---

### Task 10: Close out — Gaps.md + final verification

**Files:**
- Modify: `Gaps.md` (Accessibility section, item 4)

- [ ] **Step 1: Run the FULL suite one final time**

Run: `dotnet test DialogEditor.Tests`
Expected: all pass, zero skips.

- [ ] **Step 2: Update Gaps.md item 4** — replace the item-4 paragraph with:

```markdown
4. **Canvas keyboard editing. ✅ IMPLEMENTED (navigate + edit structure, 2026-06-12).**
   The canvas is keyboard-operable: arrows traverse the conversation topologically
   (→ child / ← parent, nearest-by-Y; ↑↓ siblings in visual order), PgUp/PgDn cycle
   every node (orphan coverage), Home selects the root, Ctrl(+Shift)+arrows nudge the
   selected node (drag-move semantics, gated on IsEditable), Enter focuses the detail
   panel, Menu key / Shift+F10 opens the node context menu, Escape deselects; canvas
   focus restores the last selection (root on first focus) and the viewport follows
   every move. Pure logic in `CanvasNavigationService` + `ConversationViewModel`
   (unit-tested); thin KeyDown glue in `ConversationView` (headless-tested); key map
   documented in plain language in the Legend window (localized). Design:
   `docs/superpowers/specs/2026-06-12-canvas-keyboard-editing-design.md`.
   **Deferred follow-up:** keyboard *connection creation* ("connect mode" — pick
   source, pick target, confirm) remains mouse-only; it needs its own interaction
   design pass.
```

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark canvas keyboard editing implemented (a11y item 4)"
```
