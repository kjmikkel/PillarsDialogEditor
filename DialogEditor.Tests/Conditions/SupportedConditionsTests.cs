using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Conditions;

public class SupportedConditionsTests
{
    private static ConditionLeaf Call(string sig, params string[] p)
        => new($"Boolean {sig}", p, Not: false, Operator: "And");

    private static Truth Eval(ConditionLeaf leaf, GameState s) => SupportedConditions.Evaluate(leaf, s);

    private static ConditionLeaf Global(string op, string v)
        => Call("IsGlobalValue(String, Operator, Int32)", "g", op, v);

    private static GameState WithGlobal(int v)
    {
        var s = new GameState();
        s.Globals["g"] = v;
        return s;
    }

    [Fact]
    public void MethodName_IsTextBetweenSpaceAndParen()
        => Assert.Equal("IsGlobalValue", SupportedConditions.MethodName(Global("EqualTo", "1")));

    [Fact]
    public void IsSupported_KnowsTheTable()
    {
        Assert.True(SupportedConditions.IsSupported("IsGlobalValue"));
        Assert.True(SupportedConditions.IsSupported("isglobalvalue"));
        Assert.False(SupportedConditions.IsSupported("IsSkillValue"));
    }

    [Fact]
    public void Globals_AreCaseSensitive_LikeTheGame()
    {
        // GlobalVariables.m_data uses the default (ordinal) comparer in both games.
        var s = new GameState();
        s.Globals["G"] = 2;
        Assert.Equal(Truth.Unknown, Eval(Global("EqualTo", "2"), s));
    }

    [Fact]
    public void IsGlobalValue_EqualTo_MatchesSetValue()
    {
        Assert.Equal(Truth.True, Eval(Global("EqualTo", "2"), WithGlobal(2)));
        Assert.Equal(Truth.False, Eval(Global("EqualTo", "2"), WithGlobal(3)));
    }

    [Fact]
    public void IsGlobalValue_Unset_IsUnknown()
        => Assert.Equal(Truth.Unknown, Eval(Global("EqualTo", "2"), new GameState()));

    [Fact]
    public void IsGlobalValue_EqualsAlias_TreatedAsEqualTo()
        => Assert.Equal(Truth.True, Eval(Global("Equals", "2"), WithGlobal(2)));

    [Fact]
    public void IsGlobalValue_BadOperator_IsUnknown()
        => Assert.Equal(Truth.Unknown, Eval(Global("Bogus", "2"), WithGlobal(2)));

    [Fact]
    public void IsGlobalValue_NonIntValue_IsUnknown()
        => Assert.Equal(Truth.Unknown, Eval(Global("EqualTo", "x"), WithGlobal(2)));

    [Fact]
    public void IsGlobalValue_WrongParameterCount_IsUnknown()
        => Assert.Equal(Truth.Unknown, Eval(Call("IsGlobalValue(String)", "g"), WithGlobal(2)));

    [Theory]
    [InlineData("GreaterThan", 3, 2, true)]
    [InlineData("GreaterThan", 2, 2, false)]
    [InlineData("LessThan", 1, 2, true)]
    [InlineData("LessThan", 2, 2, false)]
    [InlineData("NotEqualTo", 1, 2, true)]
    [InlineData("NotEqualTo", 2, 2, false)]
    [InlineData("GreaterThanOrEqualTo", 2, 2, true)]
    [InlineData("GreaterThanOrEqualTo", 1, 2, false)]
    [InlineData("LessThanOrEqualTo", 2, 2, true)]
    [InlineData("LessThanOrEqualTo", 3, 2, false)]
    public void IsGlobalValue_Operators(string op, int actual, int operand, bool expected)
        => Assert.Equal(expected ? Truth.True : Truth.False,
            Eval(Global(op, operand.ToString()), WithGlobal(actual)));

    [Fact]
    public void CompareGlobals_EitherUnset_IsUnknown()
    {
        var leaf = Call("CompareGlobals(String, Operator, String)", "a", "LessThan", "b");
        var s = new GameState();
        s.Globals["a"] = 1;
        Assert.Equal(Truth.Unknown, Eval(leaf, s));
    }

    [Fact]
    public void CompareGlobals_Compares()
    {
        var leaf = Call("CompareGlobals(String, Operator, String)", "a", "LessThan", "b");
        var s = new GameState();
        s.Globals["a"] = 1;
        s.Globals["b"] = 2;
        Assert.Equal(Truth.True, Eval(leaf, s));
        s.Globals["b"] = 1;
        Assert.Equal(Truth.False, Eval(leaf, s));
    }

    [Fact]
    public void HasConversationNodeBeenPlayed_ReadsPlayedPair()
    {
        var leaf = Call("HasConversationNodeBeenPlayed(String, Int32)", "Conv-A", "7");
        var s = new GameState();
        Assert.Equal(Truth.Unknown, Eval(leaf, s));
        s.PlayedNodes[("conv-a", 7)] = true;
        Assert.Equal(Truth.True, Eval(leaf, s));
        s.PlayedNodes[("conv-a", 7)] = false;
        Assert.Equal(Truth.False, Eval(leaf, s));
    }

    [Fact]
    public void HasConversationNodeBeenPlayed_BadInt_IsUnknown()
    {
        var s = new GameState();
        s.PlayedNodes[("c", 7)] = true;
        Assert.Equal(Truth.Unknown,
            Eval(Call("HasConversationNodeBeenPlayed(String, Int32)", "c", "x"), s));
    }

    [Fact]
    public void IsCompanionActiveInParty_ReadsCompanion()
    {
        var leaf = Call("IsCompanionActiveInParty(Guid)", "AAA");
        var s = new GameState();
        Assert.Equal(Truth.Unknown, Eval(leaf, s));
        s.CompanionsInParty["aaa"] = true;
        Assert.Equal(Truth.True, Eval(leaf, s));
        s.CompanionsInParty["aaa"] = false;
        Assert.Equal(Truth.False, Eval(leaf, s));
    }

    [Fact]
    public void IsCompanionActiveInPartyOrPresent_InParty_True_NotInParty_Unknown()
    {
        var leaf = Call("IsCompanionActiveInPartyOrPresent(Guid)", "aaa");
        var s = new GameState();
        s.CompanionsInParty["aaa"] = false;
        Assert.Equal(Truth.Unknown, Eval(leaf, s));
        s.CompanionsInParty["aaa"] = true;
        Assert.Equal(Truth.True, Eval(leaf, s));
    }

    [Fact]
    public void IsAnyCompanionActiveInParty_AnyInParty_True()
    {
        var s = new GameState();
        s.CompanionsInParty["a"] = false;
        s.CompanionsInParty["b"] = true;
        Assert.Equal(Truth.True, Eval(Call("IsAnyCompanionActiveInParty()"), s));
    }

    [Fact]
    public void IsAnyCompanionActiveInParty_NoCompanionsFlag_False()
    {
        var s = new GameState { NoCompanionsInParty = true };
        Assert.Equal(Truth.False, Eval(Call("IsAnyCompanionActiveInParty()"), s));
    }

    [Fact]
    public void IsAnyCompanionActiveInParty_OnlyNotInParty_IsUnknown()
    {
        var s = new GameState();
        s.CompanionsInParty["a"] = false;
        Assert.Equal(Truth.Unknown, Eval(Call("IsAnyCompanionActiveInParty()"), s));
    }

    [Theory]
    [InlineData("HasQuestStarted", QuestStatus.NotStarted, Truth.False)]
    [InlineData("HasQuestStarted", QuestStatus.Active, Truth.True)]
    [InlineData("HasQuestStarted", QuestStatus.Completed, Truth.True)]
    [InlineData("HasQuestStarted", QuestStatus.Failed, Truth.True)]
    [InlineData("IsQuestActive", QuestStatus.NotStarted, Truth.False)]
    [InlineData("IsQuestActive", QuestStatus.Active, Truth.True)]
    [InlineData("IsQuestActive", QuestStatus.Completed, Truth.False)]
    [InlineData("IsQuestActive", QuestStatus.Failed, Truth.Unknown)]
    [InlineData("IsQuestCompleted", QuestStatus.NotStarted, Truth.False)]
    [InlineData("IsQuestCompleted", QuestStatus.Active, Truth.False)]
    [InlineData("IsQuestCompleted", QuestStatus.Completed, Truth.True)]
    [InlineData("IsQuestCompleted", QuestStatus.Failed, Truth.False)]
    [InlineData("IsQuestFailed", QuestStatus.NotStarted, Truth.False)]
    [InlineData("IsQuestFailed", QuestStatus.Active, Truth.False)]
    [InlineData("IsQuestFailed", QuestStatus.Completed, Truth.False)]
    [InlineData("IsQuestFailed", QuestStatus.Failed, Truth.True)]
    public void QuestFunctions_ByStatus(string fn, QuestStatus status, Truth expected)
    {
        var s = new GameState();
        s.Quests["q1"] = status;
        Assert.Equal(expected, Eval(Call($"{fn}(Guid)", "Q1"), s));
    }

    [Theory]
    [InlineData("HasQuestStarted")]
    [InlineData("IsQuestActive")]
    [InlineData("IsQuestCompleted")]
    [InlineData("IsQuestFailed")]
    public void QuestFunctions_Unset_IsUnknown(string fn)
        => Assert.Equal(Truth.Unknown, Eval(Call($"{fn}(Guid)", "q1"), new GameState()));

    [Fact]
    public void IsInCombat_ReadsFlag()
    {
        var leaf = Call("IsInCombat()");
        var s = new GameState();
        Assert.Equal(Truth.Unknown, Eval(leaf, s));
        s.InCombat = true;
        Assert.Equal(Truth.True, Eval(leaf, s));
        s.InCombat = false;
        Assert.Equal(Truth.False, Eval(leaf, s));
    }

    [Fact]
    public void Constants()
    {
        var s = new GameState();
        Assert.Equal(Truth.True, Eval(Call("AlwaysTrue()"), s));
        Assert.Equal(Truth.False, Eval(Call("AlwaysFalse()"), s));
        Assert.Equal(Truth.True, Eval(Call("Always(Boolean)", "True"), s));
        Assert.Equal(Truth.False, Eval(Call("Always(Boolean)", "false"), s));
        Assert.Equal(Truth.Unknown, Eval(Call("Always(Boolean)", "maybe"), s));
    }

    [Fact]
    public void UnsupportedFunction_IsUnknown()
        => Assert.Equal(Truth.Unknown,
            Eval(Call("IsSkillValue(Guid, Guid, Operator, Int32)", "a", "b", "EqualTo", "1"), WithGlobal(1)));

    [Fact]
    public void LeafNot_IsIgnoredByLeafEvaluation()
    {
        var leaf = Global("EqualTo", "2") with { Not = true };
        Assert.Equal(Truth.True, Eval(leaf, WithGlobal(2)));
    }

    [Fact]
    public void Evaluator_DefaultsToSupportedConditions()
    {
        var list = new ConditionNode[] { Global("EqualTo", "2") };
        Assert.Equal(Truth.True, ConditionEvaluator.Evaluate(list, WithGlobal(2), "poe2"));
        Assert.Equal(Truth.False, ConditionEvaluator.Evaluate(list, WithGlobal(3), "poe1"));
    }
}
