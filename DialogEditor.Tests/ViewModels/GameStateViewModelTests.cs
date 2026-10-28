using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class GameStateViewModelTests
{
    /// Real-looking format strings for the summary keys; everything else echoes its key.
    private sealed class SummaryStrings : IStringProvider
    {
        private static readonly Dictionary<string, string> Map = new()
        {
            ["GameState_Summary"] = "In this conversation: {0}; {1}; {2}.",
            ["GameState_SummaryPass_One"] = "{0} condition passes",
            ["GameState_SummaryPass_Other"] = "{0} conditions pass",
            ["GameState_SummaryFail_One"] = "{0} fails",
            ["GameState_SummaryFail_Other"] = "{0} fail",
            ["GameState_SummaryUnknown_One"] = "{0} can't tell",
            ["GameState_SummaryUnknown_Other"] = "{0} can't tell",
        };
        public string Get(string key) => Map.TryGetValue(key, out var v) ? v : key;
        public bool TryGet(string key, out string value) { value = Get(key); return true; }
    }

    private static ConditionLeaf Leaf(string fullName, params string[] p)
        => new(fullName, p, false, "And");

    private static ConditionLeaf Global(string name, string op, string value)
        => Leaf("Boolean IsGlobalValue(String, Operator, Int32)", name, op, value);

    private static ConditionLeaf Companion(string guid)
        => Leaf("Boolean IsCompanionActiveInParty(Guid)", guid);

    private static IReadOnlyList<IReadOnlyList<ConditionNode>> Lists(params IReadOnlyList<ConditionNode>[] l) => l;

    private readonly GameState _state = new();

    private GameStateViewModel Make(Func<string, string, string?>? lookup = null)
    {
        Loc.Configure(new SummaryStrings());
        return new GameStateViewModel(_state, lookup ?? ((_, _) => null));
    }

    [Fact]
    public void Load_BuildsRowsForTestedFactsOnly()
    {
        var vm = Make();
        vm.Load(Lists([Global("g", "EqualTo", "1"), Companion("guid-x")]), "poe2");

        Assert.Single(vm.Globals);
        Assert.Single(vm.Companions);
        Assert.Empty(vm.Quests);
        Assert.True(vm.HasGlobals);
        Assert.True(vm.HasCompanions);
        Assert.False(vm.HasQuests);
        Assert.False(vm.HasPlayedNodes);
        Assert.False(vm.TestsCombat);
    }

    [Fact]
    public void Load_InCombatCondition_ShowsCombatChoice()
    {
        var vm = Make();
        vm.Load(Lists([Leaf("Boolean IsInCombat()")]), "poe2");
        Assert.True(vm.TestsCombat);
        Assert.Equal(TriState.NotSet, vm.CombatChoice);
        vm.CombatChoice = TriState.Yes;
        Assert.Equal(true, _state.InCombat);
        vm.CombatChoice = TriState.NotSet;
        Assert.Null(_state.InCombat);
    }

    [Fact]
    public void GlobalRow_SetText_WritesState()
    {
        var vm = Make();
        vm.Load(Lists([Global("g", "EqualTo", "1")]), "poe2");
        vm.Globals[0].ValueText = "7";
        Assert.Equal(7, _state.Globals["g"]);
    }

    [Fact]
    public void GlobalRow_EmptyText_RemovesFact()
    {
        var vm = Make();
        vm.Load(Lists([Global("g", "EqualTo", "1")]), "poe2");
        vm.Globals[0].ValueText = "7";
        vm.Globals[0].ValueText = "";
        Assert.False(_state.Globals.ContainsKey("g"));
    }

    [Fact]
    public void GlobalRow_NonNumericText_LeavesTheFactUnset()
    {
        var vm = Make();
        vm.Load(Lists([Global("g", "EqualTo", "1")]), "poe2");
        var ex = Record.Exception(() => vm.Globals[0].ValueText = "abc");
        Assert.Null(ex);
        Assert.False(_state.Globals.ContainsKey("g"));
    }

    [Fact]
    public void CompanionRow_Choice_WritesState()
    {
        var vm = Make();
        vm.Load(Lists([Companion("guid-x")]), "poe2");
        var row = vm.Companions[0];

        row.Choice = TriState.Yes;
        Assert.True(_state.CompanionsInParty["guid-x"]);
        row.Choice = TriState.No;
        Assert.False(_state.CompanionsInParty["guid-x"]);
        row.Choice = TriState.NotSet;
        Assert.False(_state.CompanionsInParty.ContainsKey("guid-x"));
    }

    [Fact]
    public void QuestRow_Choice_WritesState()
    {
        var vm = Make();
        vm.Load(Lists([Leaf("Boolean IsQuestActive(Guid)", "q1")]), "poe2");
        var row = vm.Quests[0];

        row.Choice = QuestChoice.Failed;
        Assert.Equal(QuestStatus.Failed, _state.Quests["q1"]);
        row.Choice = QuestChoice.NotSet;
        Assert.False(_state.Quests.ContainsKey("q1"));
    }

    [Fact]
    public void PlayedNodeRow_Choice_WritesState()
    {
        var vm = Make();
        vm.Load(Lists([Leaf("Boolean HasConversationNodeBeenPlayed(Guid, Int32)", "conv", "4")]), "poe2");
        var row = vm.PlayedNodes[0];

        row.Choice = TriState.Yes;
        Assert.True(_state.PlayedNodes[("conv", 4)]);
        row.Choice = TriState.NotSet;
        Assert.False(_state.PlayedNodes.ContainsKey(("conv", 4)));
    }

    [Fact]
    public void ClearAll_ResetsStateAndRows()
    {
        var vm = Make();
        vm.Load(Lists([Global("g", "EqualTo", "1"), Companion("guid-x")]), "poe2");
        vm.Globals[0].ValueText = "7";
        vm.Companions[0].Choice = TriState.Yes;

        vm.ClearAllCommand.Execute(null);

        Assert.False(_state.HasAnyFact);
        Assert.Equal("", vm.Globals[0].ValueText);
        Assert.Equal(TriState.NotSet, vm.Companions[0].Choice);
    }

    [Fact]
    public void SwitchingConversation_KeepsEarlierValues()
    {
        var vm = Make();
        var first = Lists([Global("g", "EqualTo", "1")]);
        vm.Load(first, "poe2");
        vm.Globals[0].ValueText = "1";

        vm.Load(Lists([Companion("guid-x")]), "poe2");
        Assert.Empty(vm.Globals);
        Assert.Equal(1, _state.Globals["g"]);

        vm.Load(first, "poe2");
        Assert.Equal("1", vm.Globals[0].ValueText);
    }

    [Fact]
    public void Summary_CountsPassFailUnknown()
    {
        var vm = Make();
        _state.Globals["a"] = 1;
        vm.Load(Lists(
            [Global("a", "EqualTo", "1")],     // passes
            [Global("a", "EqualTo", "2")],     // fails
            [Global("a", "EqualTo", "2")],     // fails
            [Global("b", "EqualTo", "1")],     // can't tell
            []), "poe2");                      // empty lists are not counted

        Assert.Equal("In this conversation: 1 condition passes; 2 fail; 1 can't tell.", vm.Summary);
    }

    [Fact]
    public void Summary_RecomputesWhenStateChanges()
    {
        var vm = Make();
        vm.Load(Lists([Global("a", "EqualTo", "1")]), "poe2");
        Assert.Contains("0 fail", vm.Summary);

        vm.Globals[0].ValueText = "5";
        Assert.Contains("1 fails", vm.Summary);
    }

    [Fact]
    public void DisplayName_FallsBackToGuid()
    {
        var vm = Make((_, _) => null);
        vm.Load(Lists([Companion("guid-x")]), "poe2");
        Assert.Equal("guid-x", vm.Companions[0].DisplayName);
    }

    [Fact]
    public void DisplayName_UsesLookupWhenAvailable()
    {
        var vm = Make((kind, id) => kind == "Speaker" && id == "guid-x" ? "Eder" : null);
        vm.Load(Lists([Companion("guid-x")]), "poe2");
        Assert.Equal("Eder", vm.Companions[0].DisplayName);
    }
}
