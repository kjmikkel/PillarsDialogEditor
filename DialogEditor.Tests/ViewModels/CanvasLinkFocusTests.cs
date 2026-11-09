using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Issue 361: keyboard access to a selected node's links. L / Shift+L step through the selected
/// node's outgoing links in the game's order; the canvas then moves focus to that link so the
/// Menu key opens its menu.
public class CanvasLinkFocusTests
{
    public CanvasLinkFocusTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel MakeNode(int id)
    {
        var node = new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None");
        var vm = new NodeViewModel(node, new StringEntry(id, "text", ""));
        vm.Input.Owner = vm;
        vm.Output.Owner = vm;
        return vm;
    }

    // Nodes 0..6. Connections, interleaved: 0>1, 5>6, 0>2, 5>4, 0>3 (node 0 order [1,2,3]).
    private static (ConversationViewModel Canvas, NodeViewModel[] N) MakeCanvas()
    {
        var canvas = new ConversationViewModel(new StubDispatcher()) { ActiveGameId = "poe2" };
        var n = Enumerable.Range(0, 7).Select(MakeNode).ToArray();
        foreach (var x in n) canvas.Nodes.Add(x);
        foreach (var (from, to) in new[] { (0, 1), (5, 6), (0, 2), (5, 4), (0, 3) })
            canvas.Connections.Add(new ConnectionViewModel(n[from].Output, n[to].Input));
        return (canvas, n);
    }

    private static int? FocusedTarget(ConversationViewModel c) => c.FocusedLink?.Target.Owner?.NodeId;

    [Fact]
    public void FocusLink_Forward_StepsThroughSelectedNodesLinksInOrder_AndWraps()
    {
        var (canvas, n) = MakeCanvas();
        canvas.SelectNode(n[0]);

        var seen = new List<int?>();
        for (var i = 0; i < 4; i++)
        {
            Assert.True(canvas.TryFocusLink(forward: true));
            seen.Add(FocusedTarget(canvas));
        }

        Assert.Equal([1, 2, 3, 1], seen);
    }

    [Fact]
    public void FocusLink_Backward_StartsAtLastLink_AndWraps()
    {
        var (canvas, n) = MakeCanvas();
        canvas.SelectNode(n[0]);

        Assert.True(canvas.TryFocusLink(forward: false));
        Assert.Equal(3, FocusedTarget(canvas));
        canvas.TryFocusLink(forward: false);
        canvas.TryFocusLink(forward: false);
        canvas.TryFocusLink(forward: false);
        Assert.Equal(3, FocusedTarget(canvas));
    }

    [Fact]
    public void FocusLink_WithNoSelection_DoesNothing()
    {
        var (canvas, _) = MakeCanvas();

        Assert.False(canvas.TryFocusLink(forward: true));
        Assert.Null(canvas.FocusedLink);
    }

    [Fact]
    public void FocusLink_OnNodeWithoutLinks_DoesNothing()
    {
        var (canvas, n) = MakeCanvas();
        canvas.SelectNode(n[1]);

        Assert.False(canvas.TryFocusLink(forward: true));
        Assert.Null(canvas.FocusedLink);
    }

    [Fact]
    public void SelectingAnotherNode_ClearsFocusedLink()
    {
        var (canvas, n) = MakeCanvas();
        canvas.SelectNode(n[0]);
        canvas.TryFocusLink(forward: true);

        canvas.SelectNode(n[5]);

        Assert.Null(canvas.FocusedLink);
    }

    [Fact]
    public void RemovingTheFocusedLink_ClearsIt()
    {
        var (canvas, n) = MakeCanvas();
        canvas.SelectNode(n[0]);
        canvas.TryFocusLink(forward: true);
        var link = canvas.FocusedLink!;

        canvas.Connections.Remove(link);

        Assert.Null(canvas.FocusedLink);
    }

    [Fact]
    public void MovingTheFocusedLink_KeepsItFocused()
    {
        var (canvas, n) = MakeCanvas();
        canvas.IsEditable = true;
        canvas.SelectNode(n[0]);
        canvas.TryFocusLink(forward: true);
        canvas.TryFocusLink(forward: true); // 0>2
        var link = canvas.FocusedLink!;

        canvas.MoveLinkEarlierCommand.Execute(link);

        Assert.Same(link, canvas.FocusedLink);
    }

    [Fact]
    public void ClearFocusedLink_ReportsWhetherThereWasOne()
    {
        var (canvas, n) = MakeCanvas();
        canvas.SelectNode(n[0]);

        Assert.False(canvas.ClearFocusedLink());
        canvas.TryFocusLink(forward: true);
        Assert.True(canvas.ClearFocusedLink());
        Assert.Null(canvas.FocusedLink);
        Assert.Same(n[0], canvas.SelectedNode);
    }

    [Fact]
    public void Link_AutomationName_NamesSourceAndTargetNodes()
    {
        Loc.Configure(new FormatStringProvider());
        try
        {
            var (canvas, _) = MakeCanvas();
            var link = canvas.Connections.Single(c => c.Source.Owner!.NodeId == 0 && c.Target.Owner!.NodeId == 2);

            Assert.Equal("from 0 to 2", link.AutomationName);
        }
        finally { Loc.Configure(new StubStringProvider()); }
    }

    private sealed class FormatStringProvider : DialogEditor.ViewModels.Services.IStringProvider
    {
        public string Get(string key) => key == "Automation_LinkName" ? "from {0} to {1}" : key;
        public bool TryGet(string key, out string value) { value = Get(key); return true; }
    }
}
