using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Canvas links fade when the assumed game state rules them out (#32).
public class CanvasCannotFireTests
{
    public CanvasCannotFireTests() => Loc.Configure(new StubStringProvider());

    // "g EqualTo 1" is definitely False once g = 0 is set.
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

    private static (ConversationViewModel Canvas, NodeViewModel A, NodeViewModel B) MakeCanvas()
    {
        var canvas = new ConversationViewModel(new StubDispatcher()) { ActiveGameId = "poe2" };
        var a = MakeNode(0);
        var b = MakeNode(1);
        canvas.Nodes.Add(a);
        canvas.Nodes.Add(b);
        return (canvas, a, b);
    }

    private static GameState StateWithFact()
    {
        var s = new GameState();
        s.Globals["g"] = 0;
        return s;
    }

    [Fact]
    public void LinkConditionFalse_AfterStateGainsFact_CannotFire()
    {
        var (canvas, a, b) = MakeCanvas();
        var conn = new ConnectionViewModel(a.Output, b.Input, conditions: False_());
        canvas.Connections.Add(conn);
        var state = new GameState();
        canvas.AssumedState = state;
        Assert.False(conn.CannotFire);

        state.Globals["g"] = 0;
        state.NotifyChanged();

        Assert.True(conn.CannotFire);
    }

    [Fact]
    public void TargetConditionsEditedToFalse_CannotFire()
    {
        var (canvas, a, b) = MakeCanvas();
        var conn = new ConnectionViewModel(a.Output, b.Input);
        canvas.Connections.Add(conn);
        canvas.AssumedState = StateWithFact();
        Assert.False(conn.CannotFire);

        b.Conditions = False_();

        Assert.True(conn.CannotFire);
    }

    [Fact]
    public void ConnectionAddedAfterStateSet_CannotFire()
    {
        var (canvas, a, b) = MakeCanvas();
        canvas.AssumedState = StateWithFact();

        var conn = new ConnectionViewModel(a.Output, b.Input, conditions: False_());
        canvas.Connections.Add(conn);

        Assert.True(conn.CannotFire);
    }

    [Fact]
    public void EmptyState_DoesNotBlock_AndToolTipIsNull()
    {
        var (canvas, a, b) = MakeCanvas();
        var conn = new ConnectionViewModel(a.Output, b.Input, conditions: False_());
        canvas.Connections.Add(conn);
        canvas.AssumedState = new GameState();

        Assert.False(conn.CannotFire);
        Assert.Null(conn.CannotFireToolTip);
    }

    [Fact]
    public void CannotFire_HasToolTip()
    {
        var (canvas, a, b) = MakeCanvas();
        var conn = new ConnectionViewModel(a.Output, b.Input, conditions: False_());
        canvas.Connections.Add(conn);
        canvas.AssumedState = StateWithFact();

        Assert.NotNull(conn.CannotFireToolTip);
    }
}
