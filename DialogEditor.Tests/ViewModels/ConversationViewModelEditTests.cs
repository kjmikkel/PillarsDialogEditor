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

    // ── NodeComments ──────────────────────────────────────────────────────

    [Fact]
    public void SetNodeComment_WhitespaceOnly_RemovesEntry()
    {
        var vm = MakeVm();
        vm.LoadNodeComments(new Dictionary<int, string> { [1] = "existing" });
        vm.SetNodeComment(1, "   ");
        Assert.False(vm.NodeComments.ContainsKey(1));
    }

    [Fact]
    public void SetNodeComment_NewEntry_AddsToDict()
    {
        var vm = MakeVm();
        vm.SetNodeComment(5, "context");
        Assert.Equal("context", vm.NodeComments[5]);
    }

    // ── AddConnectedNode ──────────────────────────────────────────────────

    [Fact]
    public void AddConnectedNode_AppearsInNodesAndConnections()
    {
        var vm     = MakeVm();
        var parent = MakeNode(1);
        vm.AddNode(parent, new LayoutPoint(0, 0));
        vm.AddConnectedNode(parent, new LayoutPoint(250, 0));
        Assert.Equal(2, vm.Nodes.Count);
        Assert.Single(vm.Connections);
        Assert.Equal(parent.Output, vm.Connections[0].Source);
    }

    [Fact]
    public void AddConnectedNode_SetsSelectedNodeToChild()
    {
        var vm     = MakeVm();
        var parent = MakeNode(1);
        vm.AddNode(parent, new LayoutPoint(0, 0));
        vm.AddConnectedNode(parent, new LayoutPoint(250, 0));
        Assert.NotNull(vm.SelectedNode);
        Assert.NotEqual(parent, vm.SelectedNode);
    }

    [Fact]
    public void AddConnectedNode_InheritsParentProperties()
    {
        var vm     = MakeVm();
        var parent = new NodeViewModel(
            new ConversationNode(1, false, SpeakerCategory.Narrator,
                "spk-1", "lst-1", [], [], [], "Bark", "ShowOnce"),
            null);
        vm.AddNode(parent, new LayoutPoint(0, 0));
        vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

        var child = vm.Nodes.Single(n => n.NodeId != 1);
        Assert.Equal(SpeakerCategory.Narrator, child.SpeakerCategory);
        Assert.Equal("spk-1",    child.SpeakerGuid);
        Assert.Equal("lst-1",    child.ListenerGuid);
        Assert.Equal("Bark",     child.DisplayType);
        Assert.Equal("ShowOnce", child.Persistence);
    }

    [Fact]
    public void AddConnectedNode_AllocatesDistinctNodeId()
    {
        var vm     = MakeVm();
        var parent = MakeNode(1);
        vm.AddNode(parent, new LayoutPoint(0, 0));
        vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

        var child = vm.Nodes.Single(n => n.NodeId != 1);
        Assert.NotEqual(1, child.NodeId);
    }

    [Fact]
    public void UndoAddConnectedNode_FirstUndoRemovesConnectionOnly()
    {
        var vm     = MakeVm();
        var parent = MakeNode(1);
        vm.AddNode(parent, new LayoutPoint(0, 0));
        vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

        vm.Undo(); // undoes the AddConnection command
        Assert.Equal(2, vm.Nodes.Count);
        Assert.Empty(vm.Connections);
    }

    [Fact]
    public void UndoAddConnectedNode_SecondUndoRemovesChildNode()
    {
        var vm     = MakeVm();
        var parent = MakeNode(1);
        vm.AddNode(parent, new LayoutPoint(0, 0));
        vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

        vm.Undo(); // undoes AddConnection
        vm.Undo(); // undoes AddNode (child)
        Assert.Single(vm.Nodes); // only the parent remains
        Assert.Empty(vm.Connections);
    }

    [Fact]
    public void RedoAddConnectedNode_FirstRedoRestoresChildNodeOnly()
    {
        var vm     = MakeVm();
        var parent = MakeNode(1);
        vm.AddNode(parent, new LayoutPoint(0, 0));
        vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

        vm.Undo(); // undo connection
        vm.Undo(); // undo child node
        vm.Redo(); // redo AddNode — child is back, no connection yet

        Assert.Equal(2, vm.Nodes.Count);
        Assert.Empty(vm.Connections);
    }

    [Fact]
    public void RedoAddConnectedNode_SecondRedoRestoresConnection()
    {
        var vm     = MakeVm();
        var parent = MakeNode(1);
        vm.AddNode(parent, new LayoutPoint(0, 0));
        vm.AddConnectedNode(parent, new LayoutPoint(250, 0));

        vm.Undo(); // undo connection
        vm.Undo(); // undo child node
        vm.Redo(); // redo AddNode
        vm.Redo(); // redo AddConnection

        Assert.Equal(2, vm.Nodes.Count);
        Assert.Single(vm.Connections);
    }

    // ── Redo ──────────────────────────────────────────────────────────────

    [Fact]
    public void RedoAddNode_RestoresNode()
    {
        var vm   = MakeVm();
        var node = MakeNode(1);
        vm.AddNode(node, new LayoutPoint(0, 0));
        vm.Undo();
        vm.Redo();
        Assert.Contains(node, vm.Nodes);
    }

    [Fact]
    public void RedoDeleteNode_RemovesNodeAgain()
    {
        var vm = MakeVm();
        var n1 = MakeNode(1);
        var n2 = MakeNode(2);
        vm.AddNode(n1, new LayoutPoint(0, 0));
        vm.AddNode(n2, new LayoutPoint(200, 0));
        vm.AddConnection(n1.Output, n2.Input);
        vm.DeleteNode(n1);
        vm.Undo();
        vm.Redo();
        Assert.DoesNotContain(n1, vm.Nodes);
        Assert.Empty(vm.Connections);
    }

    [Fact]
    public void RedoAddConnection_RestoresConnection()
    {
        var vm = MakeVm();
        var n1 = MakeNode(1);
        var n2 = MakeNode(2);
        vm.AddNode(n1, new LayoutPoint(0, 0));
        vm.AddNode(n2, new LayoutPoint(200, 0));
        vm.AddConnection(n1.Output, n2.Input);
        vm.Undo();
        vm.Redo();
        Assert.Single(vm.Connections);
    }

    [Fact]
    public void RedoDeleteConnection_RemovesConnectionAgain()
    {
        var vm = MakeVm();
        var n1 = MakeNode(1);
        var n2 = MakeNode(2);
        vm.AddNode(n1, new LayoutPoint(0, 0));
        vm.AddNode(n2, new LayoutPoint(200, 0));
        vm.AddConnection(n1.Output, n2.Input);
        var conn = vm.Connections.Single();
        vm.DeleteConnection(conn);
        vm.Undo();
        vm.Redo();
        Assert.Empty(vm.Connections);
    }

    // ── CanUndo / CanRedo ─────────────────────────────────────────────────

    [Fact]
    public void NewOperationAfterUndo_ClearsRedoStack()
    {
        var vm = MakeVm();
        vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));
        vm.Undo();
        vm.AddNode(MakeNode(2), new LayoutPoint(0, 0));
        Assert.False(vm.CanRedo);
    }

    [Fact]
    public void CanUndoCanRedo_StateTransitions()
    {
        var vm = MakeVm();
        Assert.False(vm.CanUndo);
        Assert.False(vm.CanRedo);

        vm.AddNode(MakeNode(1), new LayoutPoint(0, 0));
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);

        vm.Undo();
        Assert.False(vm.CanUndo);
        Assert.True(vm.CanRedo);

        vm.Redo();
        Assert.True(vm.CanUndo);
        Assert.False(vm.CanRedo);
    }
}
