using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

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
}
