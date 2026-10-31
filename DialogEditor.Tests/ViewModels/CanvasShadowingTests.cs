using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Canvas links fade when an earlier link from the same node always passes (issue 353).
public class CanvasShadowingTests
{
    public CanvasShadowingTests() => Loc.Configure(new StubStringProvider());

    private static IReadOnlyList<ConditionNode> False_() =>
        [new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)", ["g", "EqualTo", "1"], Not: false, Operator: "And")];

    private static NodeViewModel MakeNode(int id)
    {
        var node = new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None");
        var vm = new NodeViewModel(node, new StringEntry(id, "text", ""));
        vm.Input.Owner = vm;
        vm.Output.Owner = vm;
        return vm;
    }

    // Source 0 with targets 1, 2, 3 (all NPC lines).
    private static (ConversationViewModel Canvas, NodeViewModel[] N) MakeCanvas()
    {
        var canvas = new ConversationViewModel(new StubDispatcher()) { ActiveGameId = "poe2" };
        var n = Enumerable.Range(0, 4).Select(MakeNode).ToArray();
        foreach (var x in n) canvas.Nodes.Add(x);
        return (canvas, n);
    }

    private static ConnectionViewModel Link(ConversationViewModel canvas, NodeViewModel[] n, int to,
        IReadOnlyList<ConditionNode>? conditions = null)
    {
        var c = new ConnectionViewModel(n[0].Output, n[to].Input, conditions: conditions);
        canvas.Connections.Add(c);
        return c;
    }

    [Fact]
    public void UnconditionalFirstLink_FadesLaterLinks()
    {
        var (canvas, n) = MakeCanvas();
        var first = Link(canvas, n, 1);
        var second = Link(canvas, n, 2);
        var third = Link(canvas, n, 3);

        Assert.False(first.IsFaded);
        Assert.True(second.IsFaded);
        Assert.True(third.IsFaded);
        Assert.Equal(1, second.ShadowedByNodeId);
        Assert.NotNull(second.FadedToolTip);
    }

    [Fact]
    public void AddingConditionToFirstLink_UnfadesLaterLinks()
    {
        var (canvas, n) = MakeCanvas();
        var first = Link(canvas, n, 1);
        var second = Link(canvas, n, 2);
        Assert.True(second.IsFaded);

        first.Conditions = False_();

        Assert.False(second.IsFaded);
    }

    [Fact]
    public void TargetPersistenceOncePerConversation_UnfadesLaterLinks()
    {
        var (canvas, n) = MakeCanvas();
        Link(canvas, n, 1);
        var second = Link(canvas, n, 2);
        Assert.True(second.IsFaded);

        n[1].Persistence = "OncePerConversation";

        Assert.False(second.IsFaded);
    }

    [Fact]
    public void AssumedStateMakesFirstLinkCertain_FadesLaterLinks()
    {
        var (canvas, n) = MakeCanvas();
        Link(canvas, n, 1, [new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)", ["g", "EqualTo", "0"], Not: false, Operator: "And")]);
        var second = Link(canvas, n, 2);
        Assert.False(second.IsFaded);

        var state = new GameState();
        canvas.AssumedState = state;
        state.Globals["g"] = 0;
        state.NotifyChanged();

        Assert.True(second.IsFaded);
    }

    [Fact]
    public void NewLinkAfterCertainLink_IsFadedOnAdd()
    {
        var (canvas, n) = MakeCanvas();
        Link(canvas, n, 1);

        var added = Link(canvas, n, 2);

        Assert.True(added.IsFaded);
    }

    [Fact]
    public void DeletingTheHider_UnfadesTheRest()
    {
        var (canvas, n) = MakeCanvas();
        var first = Link(canvas, n, 1);
        var second = Link(canvas, n, 2);
        Assert.True(second.IsFaded);

        canvas.DeleteConnection(first);

        Assert.False(second.IsFaded);
    }

    [Fact]
    public void UndoDeleteLink_RestoresLinkOrder()
    {
        var (canvas, n) = MakeCanvas();
        var first = Link(canvas, n, 1);
        Link(canvas, n, 2);
        Link(canvas, n, 3);

        canvas.DeleteConnection(first);
        canvas.Undo();

        var order = canvas.BuildSnapshot().Nodes.First(x => x.NodeId == 0).Links.Select(l => l.ToNodeId);
        Assert.Equal([1, 2, 3], order);
        Assert.True(canvas.Connections[2].IsFaded);
    }

    [Fact]
    public void CannotFireTooltipWinsOverShadowed()
    {
        var (canvas, n) = MakeCanvas();
        Link(canvas, n, 1);
        var second = Link(canvas, n, 2, False_());
        var state = new GameState();
        state.Globals["g"] = 0;
        canvas.AssumedState = state;

        Assert.True(second.CannotFire);
        Assert.Equal(second.CannotFireToolTip, second.FadedToolTip);
    }
}
