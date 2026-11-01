using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Move Link Earlier / Later on the canvas swaps a link with its neighbour of the same node (issue 359).
public class CanvasMoveLinkTests
{
    public CanvasMoveLinkTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel MakeNode(int id)
    {
        var node = new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None");
        var vm = new NodeViewModel(node, new StringEntry(id, "text", ""));
        vm.Input.Owner = vm;
        vm.Output.Owner = vm;
        return vm;
    }

    // Nodes 0..6. Connections, interleaved: 0>1, 5>6, 0>2, 5>4, 0>3 (node 0 order [1,2,3], node 5 order [6,4]).
    private static (ConversationViewModel Canvas, NodeViewModel[] N) MakeCanvas()
    {
        var canvas = new ConversationViewModel(new StubDispatcher()) { ActiveGameId = "poe2" };
        var n = Enumerable.Range(0, 7).Select(MakeNode).ToArray();
        foreach (var x in n) canvas.Nodes.Add(x);
        canvas.IsEditable = true;
        foreach (var (from, to) in new[] { (0, 1), (5, 6), (0, 2), (5, 4), (0, 3) })
            canvas.Connections.Add(new ConnectionViewModel(n[from].Output, n[to].Input));
        return (canvas, n);
    }

    private static ConnectionViewModel Find(ConversationViewModel c, int from, int to) =>
        c.Connections.Single(x => x.Source.Owner!.NodeId == from && x.Target.Owner!.NodeId == to);

    private static int[] Order(ConversationViewModel c, int from) =>
        c.Connections.Where(x => x.Source.Owner!.NodeId == from).Select(x => x.Target.Owner!.NodeId).ToArray();

    [Fact]
    public void MoveLinkEarlier_SwapsWithPreviousSiblingOnly()
    {
        var (canvas, _) = MakeCanvas();
        var others = canvas.Connections.Select((c, i) => (c, i)).Where(x => x.c.Source.Owner!.NodeId == 5).ToList();

        canvas.MoveLinkEarlierCommand.Execute(Find(canvas, 0, 3));

        Assert.Equal([1, 3, 2], Order(canvas, 0));
        Assert.Equal([6, 4], Order(canvas, 5));
        foreach (var (c, i) in others) Assert.Equal(i, canvas.Connections.IndexOf(c));
    }

    [Fact]
    public void MoveLinkLater_SwapsWithNextSiblingOnly()
    {
        var (canvas, _) = MakeCanvas();
        var others = canvas.Connections.Select((c, i) => (c, i)).Where(x => x.c.Source.Owner!.NodeId == 5).ToList();

        canvas.MoveLinkLaterCommand.Execute(Find(canvas, 0, 1));

        Assert.Equal([2, 1, 3], Order(canvas, 0));
        foreach (var (c, i) in others) Assert.Equal(i, canvas.Connections.IndexOf(c));
    }

    [Fact]
    public void CanMove_FalseAtTheEndsAndForNull()
    {
        var (canvas, _) = MakeCanvas();

        Assert.False(canvas.CanMoveLinkEarlier(Find(canvas, 0, 1)));
        Assert.True(canvas.CanMoveLinkEarlier(Find(canvas, 0, 2)));
        Assert.True(canvas.CanMoveLinkLater(Find(canvas, 0, 2)));
        Assert.False(canvas.CanMoveLinkLater(Find(canvas, 0, 3)));
        Assert.False(canvas.CanMoveLinkEarlier(null));
        Assert.False(canvas.CanMoveLinkLater(null));
        // A node's only link has nowhere to go.
        canvas.DeleteConnection(Find(canvas, 5, 4));
        Assert.False(canvas.CanMoveLinkEarlier(Find(canvas, 5, 6)));
        Assert.False(canvas.CanMoveLinkLater(Find(canvas, 5, 6)));
    }

    [Fact]
    public void CanMove_FalseWhenNotEditable()
    {
        var (canvas, _) = MakeCanvas();
        canvas.IsEditable = false;

        Assert.False(canvas.CanMoveLinkEarlier(Find(canvas, 0, 2)));
        Assert.False(canvas.CanMoveLinkLater(Find(canvas, 0, 2)));
    }

    [Fact]
    public void MoveLink_UndoRedo_RestoresExactOrder()
    {
        var (canvas, _) = MakeCanvas();
        canvas.IsModified = false;
        var original = canvas.Connections.ToList();

        canvas.MoveLinkEarlierCommand.Execute(Find(canvas, 0, 3));
        var moved = canvas.Connections.ToList();

        Assert.True(canvas.IsModified);
        Assert.Equal("Undo_MoveLink", canvas.UndoDescription);

        canvas.Undo();
        Assert.Equal(original, canvas.Connections.ToList());

        canvas.Redo();
        Assert.Equal(moved, canvas.Connections.ToList());
    }

    [Fact]
    public void MoveLink_RefreshesShadowing()
    {
        var (canvas, n) = MakeCanvas();
        canvas.Connections.Clear();
        var one = new ConnectionViewModel(n[0].Output, n[1].Input);
        var two = new ConnectionViewModel(n[0].Output, n[2].Input);
        canvas.Connections.Add(one);
        canvas.Connections.Add(two);
        Assert.True(two.IsFaded);
        Assert.False(one.IsFaded);

        canvas.MoveLinkEarlierCommand.Execute(two);

        Assert.False(two.IsFaded);
        Assert.True(one.IsFaded);

        canvas.Undo();

        Assert.True(two.IsFaded);
        Assert.False(one.IsFaded);
    }

    [Fact]
    public void ReorderedNode_SavesNewOrder_InTheSnapshot()
    {
        var (canvas, _) = MakeCanvas();

        canvas.MoveLinkEarlierCommand.Execute(Find(canvas, 0, 3));

        var order = canvas.BuildSnapshot().Nodes.First(x => x.NodeId == 0).Links.Select(l => l.ToNodeId);
        Assert.Equal([1, 3, 2], order);
    }
}
