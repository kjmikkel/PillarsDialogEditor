using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class ConversationViewModelEditTests
{
    public ConversationViewModelEditTests()
    {
        Loc.Configure(new StubStringProvider());
    }

    private static ConversationViewModel MakeVm() =>
        new(new StubDispatcher());

    private static NodeViewModel MakeNode(int id) =>
        new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);

    [Fact]
    public void AddNode_AppearsInNodes()
    {
        var vm   = MakeVm();
        var node = MakeNode(5);
        vm.AddNode(node, new LayoutPoint(0, 0));
        Assert.Contains(node, vm.Nodes);
    }

    [Fact]
    public void AddNode_SetsIsModified()
    {
        var vm = MakeVm();
        vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));
        Assert.True(vm.IsModified);
    }

    [Fact]
    public void UndoAddNode_RemovesNode()
    {
        var vm   = MakeVm();
        var node = MakeNode(1);
        vm.AddNode(node, new LayoutPoint(0, 0));
        vm.Undo();
        Assert.DoesNotContain(node, vm.Nodes);
    }

    [Fact]
    public void DeleteNode_RemovesNodeAndItsConnections()
    {
        var vm = MakeVm();
        var n1 = MakeNode(1);
        var n2 = MakeNode(2);
        vm.AddNode(n1, new LayoutPoint(0, 0));
        vm.AddNode(n2, new LayoutPoint(200, 0));
        vm.AddConnection(n1.Output, n2.Input);
        vm.DeleteNode(n1);
        Assert.DoesNotContain(n1, vm.Nodes);
        Assert.Empty(vm.Connections);
    }

    [Fact]
    public void UndoDeleteNode_RestoresNodeAndConnections()
    {
        var vm = MakeVm();
        var n1 = MakeNode(1);
        var n2 = MakeNode(2);
        vm.AddNode(n1, new LayoutPoint(0, 0));
        vm.AddNode(n2, new LayoutPoint(200, 0));
        vm.AddConnection(n1.Output, n2.Input);
        vm.DeleteNode(n1);
        vm.Undo();
        Assert.Contains(n1, vm.Nodes);
        Assert.Single(vm.Connections);
    }

    [Fact]
    public void Load_ClearsIsModified()
    {
        var vm = MakeVm();
        vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));
        Assert.True(vm.IsModified);
        vm.Load(new Conversation("test", [], StringTable.Empty));
        Assert.False(vm.IsModified);
    }

    [Fact]
    public void BuildSnapshot_ReflectsCurrentNodes()
    {
        var vm = MakeVm();
        vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));
        vm.AddNode(MakeNode(2), new LayoutPoint(200, 0));
        var snapshot = vm.BuildSnapshot();
        Assert.Equal(2, snapshot.Nodes.Count);
        Assert.Contains(snapshot.Nodes, n => n.NodeId == 1);
        Assert.Contains(snapshot.Nodes, n => n.NodeId == 2);
    }

    [Fact]
    public void BuildSnapshot_IncludesConnections()
    {
        var vm = MakeVm();
        var n1 = MakeNode(1);
        var n2 = MakeNode(2);
        vm.AddNode(n1, new LayoutPoint(0, 0));
        vm.AddNode(n2, new LayoutPoint(200, 0));
        vm.AddConnection(n1.Output, n2.Input);
        var snapshot = vm.BuildSnapshot();
        var node1Snap = snapshot.Nodes.First(n => n.NodeId == 1);
        Assert.Single(node1Snap.Links);
        Assert.Equal(2, node1Snap.Links[0].ToNodeId);
    }
}
