using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class NodeChangeViewModelTests
{
    [Fact]
    public void IsSelected_DefaultsFalse()
    {
        var vm = new NodeChangeViewModel(1, DiffStatus.Added);
        Assert.False(vm.IsSelected);
    }

    [Fact]
    public void IsSelected_SetTrue_FiresSelectionChanged()
    {
        var vm = new NodeChangeViewModel(1, DiffStatus.Added);
        var fired = false;
        vm.SelectionChanged += () => fired = true;
        vm.IsSelected = true;
        Assert.True(fired);
    }

    [Fact]
    public void IsSelected_SetSameValue_DoesNotFireSelectionChanged()
    {
        var vm = new NodeChangeViewModel(1, DiffStatus.Added);
        var count = 0;
        vm.SelectionChanged += () => count++;
        vm.IsSelected = false; // already false
        Assert.Equal(0, count);
    }

    [Fact]
    public void Kind_IsPreserved()
    {
        var vm = new NodeChangeViewModel(7, DiffStatus.Removed);
        Assert.Equal(DiffStatus.Removed, vm.Kind);
        Assert.Equal(7, vm.NodeId);
    }
}

public class ConversationChangeViewModelTests
{
    private static ConversationChange MakeChange(
        int[] added, int[] modified, int[] removed, string name = "conv")
        => new(name, added, removed, modified);

    // ── Construction ──────────────────────────────────────────────────────

    [Fact]
    public void Nodes_ContainsAllChangedIds()
    {
        var vm = new ConversationChangeViewModel(MakeChange([1], [2], [3]));
        Assert.Equal(3, vm.Nodes.Count);
    }

    [Fact]
    public void Nodes_KindsMatchSourceLists()
    {
        var vm = new ConversationChangeViewModel(MakeChange(added: [10], modified: [20], removed: [30]));
        Assert.Contains(vm.Nodes, n => n.NodeId == 10 && n.Kind == DiffStatus.Added);
        Assert.Contains(vm.Nodes, n => n.NodeId == 20 && n.Kind == DiffStatus.Changed);
        Assert.Contains(vm.Nodes, n => n.NodeId == 30 && n.Kind == DiffStatus.Removed);
    }

    // ── IsAllSelected tri-state ───────────────────────────────────────────

    [Fact]
    public void IsAllSelected_WhenNoneSelected_ReturnsFalse()
    {
        var vm = new ConversationChangeViewModel(MakeChange([1, 2], [], []));
        Assert.Equal(false, vm.IsAllSelected);
    }

    [Fact]
    public void IsAllSelected_WhenAllSelected_ReturnsTrue()
    {
        var vm = new ConversationChangeViewModel(MakeChange([1, 2], [], []));
        foreach (var n in vm.Nodes) n.IsSelected = true;
        Assert.Equal(true, vm.IsAllSelected);
    }

    [Fact]
    public void IsAllSelected_WhenSomeSelected_ReturnsNull()
    {
        var vm = new ConversationChangeViewModel(MakeChange([1, 2], [], []));
        vm.Nodes[0].IsSelected = true;
        Assert.Null(vm.IsAllSelected);
    }

    [Fact]
    public void IsAllSelected_SetTrue_SelectsAllNodes()
    {
        var vm = new ConversationChangeViewModel(MakeChange([1, 2, 3], [], []));
        vm.IsAllSelected = true;
        Assert.All(vm.Nodes, n => Assert.True(n.IsSelected));
    }

    [Fact]
    public void IsAllSelected_SetFalse_DeselectsAllNodes()
    {
        var vm = new ConversationChangeViewModel(MakeChange([1, 2], [], []));
        vm.IsAllSelected = true;
        vm.IsAllSelected = false;
        Assert.All(vm.Nodes, n => Assert.False(n.IsSelected));
    }

    [Fact]
    public void IsAllSelected_SetNull_IsNoOp()
    {
        var vm = new ConversationChangeViewModel(MakeChange([1, 2], [], []));
        vm.IsAllSelected = true;
        vm.IsAllSelected = null; // should be ignored
        Assert.Equal(true, vm.IsAllSelected);
    }

    // ── SelectionChanged propagation ──────────────────────────────────────

    [Fact]
    public void SelectionChanged_FiredWhenNodeToggles()
    {
        var vm = new ConversationChangeViewModel(MakeChange([1], [], []));
        var fired = false;
        vm.SelectionChanged += () => fired = true;
        vm.Nodes[0].IsSelected = true;
        Assert.True(fired);
    }

    [Fact]
    public void SelectionChanged_FiredWhenIsAllSelectedSet()
    {
        var vm = new ConversationChangeViewModel(MakeChange([1, 2], [], []));
        var count = 0;
        vm.SelectionChanged += () => count++;
        vm.IsAllSelected = true;
        Assert.True(count > 0);
    }

    // ── SelectedNodeIds ───────────────────────────────────────────────────

    [Fact]
    public void SelectedNodeIds_ReturnsOnlySelectedIds()
    {
        var vm = new ConversationChangeViewModel(MakeChange([1, 2, 3], [], []));
        vm.Nodes[0].IsSelected = true;
        vm.Nodes[2].IsSelected = true;
        Assert.Equal([1, 3], vm.SelectedNodeIds);
    }

    // ── AutoPull dependency closure ───────────────────────────────────────

    [Fact]
    public void AutoPull_WhenEnabled_TickingNodeAlsoPullsLinkedAddedNodes()
    {
        // Node 1 (added) links to node 2 (added)
        var vm = new ConversationChangeViewModel(MakeChange(added: [1, 2], modified: [], removed: []));
        vm.AutoPullEnabled = true;
        vm.SetDependencies(
            outgoing: new Dictionary<int, IReadOnlyList<int>> { [1] = [2] },
            addedIds:  new HashSet<int> { 1, 2 });

        vm.Nodes.First(n => n.NodeId == 1).IsSelected = true;

        Assert.True(vm.Nodes.First(n => n.NodeId == 2).IsSelected);
    }

    [Fact]
    public void AutoPull_WhenDisabled_TickingNodeDoesNotPullDependencies()
    {
        var vm = new ConversationChangeViewModel(MakeChange(added: [1, 2], modified: [], removed: []));
        vm.AutoPullEnabled = false;
        vm.SetDependencies(
            outgoing: new Dictionary<int, IReadOnlyList<int>> { [1] = [2] },
            addedIds:  new HashSet<int> { 1, 2 });

        vm.Nodes.First(n => n.NodeId == 1).IsSelected = true;

        Assert.False(vm.Nodes.First(n => n.NodeId == 2).IsSelected);
    }

    [Fact]
    public void AutoPull_DoesNotPullNonAddedNodes()
    {
        // Node 1 (added) links to node 3 (modified, not in addedIds)
        var vm = new ConversationChangeViewModel(MakeChange(added: [1], modified: [3], removed: []));
        vm.AutoPullEnabled = true;
        vm.SetDependencies(
            outgoing: new Dictionary<int, IReadOnlyList<int>> { [1] = [3] },
            addedIds:  new HashSet<int> { 1 }); // 3 is not added

        vm.Nodes.First(n => n.NodeId == 1).IsSelected = true;

        Assert.False(vm.Nodes.First(n => n.NodeId == 3).IsSelected);
    }
}
