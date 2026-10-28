using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailVerdictTests
{
    private readonly NodeDetailViewModel _vm = new();
    private readonly GameState _state = new();

    public NodeDetailVerdictTests()
    {
        Loc.Configure(new StubStringProvider());
        _vm.ActiveGameId = "poe2";
        _vm.AssumedState = _state;
    }

    private static ConditionLeaf Global(string name, int value)
        => new("Boolean IsGlobalValue(String, Operator, Int32)",
               [name, "EqualTo", value.ToString()], false, "And");

    private static NodeViewModel MakeNode(params ConditionNode[] conditions)
    {
        var node = new ConversationNode(NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: conditions, Scripts: [],
            DisplayType: "Conversation", Persistence: "None", ActorDirection: "", Comments: "",
            ExternalVO: "", HasVO: false, HideSpeaker: false);
        return new NodeViewModel(node, new StringEntry(1, "Hello", ""));
    }

    [Fact]
    public void NoFactsSet_HidesVerdict()
    {
        _vm.Load(MakeNode(Global("g", 1)));
        Assert.False(_vm.ShowConditionVerdict);
    }

    [Fact]
    public void NodeWithoutConditions_HidesVerdict()
    {
        _state.Globals["g"] = 1;
        _vm.Load(MakeNode());
        Assert.False(_vm.ShowConditionVerdict);
    }

    [Fact]
    public void FactSet_NodeConditionsPass_ShowsPasses()
    {
        _state.Globals["g"] = 1;
        _vm.Load(MakeNode(Global("g", 1)));

        Assert.True(_vm.ShowConditionVerdict);
        Assert.Equal(Truth.True, _vm.ConditionVerdict);
        Assert.Equal("NodeDetail_Verdict_Passes", _vm.ConditionVerdictText);
    }

    [Fact]
    public void Fails()
    {
        _state.Globals["g"] = 2;
        _vm.Load(MakeNode(Global("g", 1)));

        Assert.Equal(Truth.False, _vm.ConditionVerdict);
        Assert.Equal("NodeDetail_Verdict_Fails", _vm.ConditionVerdictText);
    }

    [Fact]
    public void CantTell()
    {
        _state.Globals["other"] = 2;   // a fact is set, but not the one this node tests
        _vm.Load(MakeNode(Global("g", 1)));

        Assert.True(_vm.ShowConditionVerdict);
        Assert.Equal(Truth.Unknown, _vm.ConditionVerdict);
        Assert.Equal("NodeDetail_Verdict_Unknown", _vm.ConditionVerdictText);
    }

    [Fact]
    public void LinkWithConditions_ShowsItsOwnVerdict()
    {
        _state.Globals["g"] = 1;
        var withConditions = new ConnectionViewModel(new ConnectorViewModel(), new ConnectorViewModel(),
            conditions: [Global("g", 1)]);
        var failing = new ConnectionViewModel(new ConnectorViewModel(), new ConnectorViewModel(),
            conditions: [Global("g", 9)]);
        var plain = new ConnectionViewModel(new ConnectorViewModel(), new ConnectorViewModel());

        _vm.Load(MakeNode());
        _vm.RefreshLinks([withConditions, failing, plain]);

        Assert.True(withConditions.ShowConditionVerdict);
        Assert.Equal(Truth.True, withConditions.ConditionVerdict);
        Assert.Equal("NodeDetail_Verdict_Passes", withConditions.ConditionVerdictText);
        Assert.Equal(Truth.False, failing.ConditionVerdict);
        Assert.False(plain.ShowConditionVerdict);
    }

    [Fact]
    public void StateChanged_UpdatesVerdictLive()
    {
        _vm.Load(MakeNode(Global("g", 1)));
        var link = new ConnectionViewModel(new ConnectorViewModel(), new ConnectorViewModel(),
            conditions: [Global("g", 1)]);
        _vm.RefreshLinks([link]);
        Assert.False(_vm.ShowConditionVerdict);

        var raised = new List<string?>();
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        var linkRaised = new List<string?>();
        link.PropertyChanged += (_, e) => linkRaised.Add(e.PropertyName);

        _state.Globals["g"] = 1;
        _state.NotifyChanged();

        Assert.True(_vm.ShowConditionVerdict);
        Assert.Equal(Truth.True, _vm.ConditionVerdict);
        Assert.Contains(nameof(NodeDetailViewModel.ConditionVerdictText), raised);
        Assert.Equal(Truth.True, link.ConditionVerdict);
        Assert.Contains(nameof(ConnectionViewModel.ConditionVerdictText), linkRaised);
    }

    [Fact]
    public void ReplacingTheState_StopsListeningToTheOldOne()
    {
        _vm.Load(MakeNode(Global("g", 1)));
        var other = new GameState();
        _vm.AssumedState = other;

        var raised = 0;
        _vm.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(NodeDetailViewModel.ConditionVerdict)) raised++; };
        _state.Globals["g"] = 1;
        _state.NotifyChanged();

        Assert.Equal(0, raised);
    }

    [Fact]
    public void EditingTheNodesConditions_UpdatesVerdict()
    {
        _state.Globals["g"] = 1;
        var node = MakeNode(Global("g", 1));
        _vm.Load(node);
        Assert.Equal(Truth.True, _vm.ConditionVerdict);

        node.Conditions = [Global("g", 5)];

        Assert.Equal(Truth.False, _vm.ConditionVerdict);
    }
}
