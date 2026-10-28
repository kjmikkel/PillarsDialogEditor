using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Conditions;

public class ConditionFactsTests
{
    private static ConditionLeaf Call(string sig, params string[] p)
        => new($"Boolean {sig}", p, Not: false, Operator: "And");

    [Fact]
    public void Collect_RecursesBranches_DistinctInOrder()
    {
        var nodes = new ConditionNode[]
        {
            Call("IsGlobalValue(String, Operator, Int32)", "g1", "EqualTo", "1"),
            new ConditionBranch([
                Call("IsCompanionActiveInParty(Guid)", "c1"),
                Call("IsGlobalValue(String, Operator, Int32)", "g1", "EqualTo", "2"),
            ], false, "And"),
            Call("HasQuestStarted(Guid)", "q1"),
            Call("HasConversationNodeBeenPlayed(String, Int32)", "cv", "5"),
        };
        var facts = ConditionFacts.Collect(nodes);
        Assert.Equal(new[]
        {
            new ConditionFact(FactKind.Global, "g1"),
            new ConditionFact(FactKind.Companion, "c1"),
            new ConditionFact(FactKind.Quest, "q1"),
            new ConditionFact(FactKind.PlayedNode, "cv", 5),
        }, facts);
    }

    [Fact]
    public void Collect_CompareGlobals_YieldsBoth()
    {
        var facts = ConditionFacts.Collect([Call("CompareGlobals(String, Operator, String)", "a", "LessThan", "b")]);
        Assert.Equal(new[] { new ConditionFact(FactKind.Global, "a"), new ConditionFact(FactKind.Global, "b") }, facts);
    }

    [Fact]
    public void Collect_IgnoresUnsupported()
        => Assert.Empty(ConditionFacts.Collect([Call("IsSkillValue(Guid, Guid, Operator, Int32)", "a", "b", "EqualTo", "1")]));

    [Fact]
    public void Collect_IsInCombat_YieldsOneFact()
    {
        var facts = ConditionFacts.Collect([Call("IsInCombat()"), Call("IsInCombat()")]);
        Assert.Equal(new[] { new ConditionFact(FactKind.InCombat, "") }, facts);
    }
}
