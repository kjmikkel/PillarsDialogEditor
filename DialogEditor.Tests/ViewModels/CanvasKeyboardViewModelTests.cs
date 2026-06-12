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
