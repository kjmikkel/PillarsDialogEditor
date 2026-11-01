using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// While a node is selected its links show their place in the node's link order (issue 359).
public class CanvasLinkPositionTests
{
    public CanvasLinkPositionTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel MakeNode(int id)
    {
        var node = new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None");
        var vm = new NodeViewModel(node, new StringEntry(id, "text", ""));
        vm.Input.Owner = vm;
        vm.Output.Owner = vm;
        return vm;
    }

    // Connections, interleaved: 0>1, 5>6, 0>2, 5>4, 0>3 (node 0 order [1,2,3], node 5 order [6,4]).
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

    private static int[] Positions(ConversationViewModel c, int from) =>
        c.Connections.Where(x => x.Source.Owner!.NodeId == from).Select(x => x.LinkPosition).ToArray();

    [Fact]
    public void Positions_FollowEachSourcesOwnOrder_AndCount()
    {
        var (canvas, _) = MakeCanvas();

        Assert.Equal([1, 2, 3], Positions(canvas, 0));
        Assert.Equal([1, 2], Positions(canvas, 5));
        Assert.All(canvas.Connections.Where(c => c.Source.Owner!.NodeId == 0), c => Assert.Equal(3, c.LinkCount));
        Assert.All(canvas.Connections.Where(c => c.Source.Owner!.NodeId == 5), c => Assert.Equal(2, c.LinkCount));
    }

    [Fact]
    public void Delete_RenumbersTheRemainingLinks()
    {
        var (canvas, _) = MakeCanvas();

        canvas.DeleteConnection(Find(canvas, 0, 1));

        Assert.Equal([1, 2], Positions(canvas, 0));
        Assert.Equal(2, Find(canvas, 0, 2).LinkCount);
        Assert.Equal([1, 2], Positions(canvas, 5));
    }

    [Fact]
    public void Move_AndUndo_UpdatePositions()
    {
        var (canvas, _) = MakeCanvas();
        var three = Find(canvas, 0, 3);

        canvas.MoveLinkEarlierCommand.Execute(three);
        Assert.Equal(2, three.LinkPosition);
        Assert.Equal(3, Find(canvas, 0, 2).LinkPosition);

        canvas.Undo();
        Assert.Equal(3, three.LinkPosition);
        Assert.Equal(2, Find(canvas, 0, 2).LinkPosition);
    }

    [Fact]
    public void Add_GivesTheNewLinkTheLastPlace()
    {
        var (canvas, n) = MakeCanvas();

        var added = new ConnectionViewModel(n[5].Output, n[2].Input);
        canvas.Connections.Add(added);

        Assert.Equal(3, added.LinkPosition);
        Assert.Equal(3, Find(canvas, 5, 6).LinkCount);
    }

    [Fact]
    public void ShowLinkPosition_FollowsSourceSelection()
    {
        var (canvas, n) = MakeCanvas();
        var link = Find(canvas, 0, 2);
        var changes = new List<string?>();
        link.PropertyChanged += (_, e) => changes.Add(e.PropertyName);

        Assert.False(link.ShowLinkPosition);

        n[0].IsSelected = true;
        Assert.True(link.ShowLinkPosition);
        Assert.Contains(nameof(ConnectionViewModel.ShowLinkPosition), changes);
        Assert.False(Find(canvas, 5, 6).ShowLinkPosition);

        n[0].IsSelected = false;
        Assert.False(link.ShowLinkPosition);
    }

    [Fact]
    public void ShowLinkPosition_FalseWithOneLink_EvenWhenSelected()
    {
        var (canvas, n) = MakeCanvas();
        canvas.DeleteConnection(Find(canvas, 5, 4));
        n[5].IsSelected = true;

        Assert.False(Find(canvas, 5, 6).ShowLinkPosition);

        // Gaining a second link while selected shows the numbers at once.
        canvas.Connections.Add(new ConnectionViewModel(n[5].Output, n[2].Input));
        Assert.True(Find(canvas, 5, 6).ShowLinkPosition);
    }

    [Fact]
    public void HelpText_SaysPositionAndCount_OnlyWithTwoOrMoreLinks()
    {
        var (canvas, n) = MakeCanvas();
        var link = Find(canvas, 0, 2);

        Assert.Equal(Loc.Format("Automation_LinkPosition", 2, 3), link.LinkPositionHelpText);
        Assert.Equal(link.LinkPositionHelpText, link.LinkAutomationHelpText);

        canvas.DeleteConnection(Find(canvas, 5, 4));
        Assert.Null(Find(canvas, 5, 6).LinkAutomationHelpText);
    }
}
