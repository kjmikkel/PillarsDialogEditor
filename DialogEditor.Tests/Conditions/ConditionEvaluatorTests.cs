using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Conditions;

public class ConditionEvaluatorTests
{
    private static ConditionLeaf Leaf(string name, string op = "And", bool not = false)
        => new($"Boolean {name}", [], not, op);

    private static ConditionLeaf T(string op = "And", bool not = false) => Leaf("T()", op, not);
    private static ConditionLeaf F(string op = "And", bool not = false) => Leaf("F()", op, not);
    private static ConditionLeaf U(string op = "And", bool not = false) => Leaf("U()", op, not);
    private static ConditionLeaf U2(string op = "And", bool not = false) => Leaf("U2()", op, not);

    private static Truth Stub(ConditionLeaf l, GameState s) => l.FullName switch
    {
        "Boolean T()" => Truth.True,
        "Boolean F()" => Truth.False,
        _ => Truth.Unknown,
    };

    private static Truth Eval(string game, params ConditionNode[] nodes)
        => ConditionEvaluator.Evaluate(nodes, new GameState(), game, Stub);

    [Fact]
    public void EmptyList_IsTrue() => Assert.Equal(Truth.True, Eval("poe2"));

    [Fact]
    public void NotOnLeaf_FlipsTrueAndFalse_LeavesUnknown()
    {
        Assert.Equal(Truth.False, Eval("poe2", T(not: true)));
        Assert.Equal(Truth.True, Eval("poe2", F(not: true)));
        Assert.Equal(Truth.Unknown, Eval("poe2", U(not: true)));
    }

    [Fact]
    public void UnknownAndFalse_IsFalse() => Assert.Equal(Truth.False, Eval("poe2", U("And"), F()));

    [Fact]
    public void UnknownOrTrue_IsTrue() => Assert.Equal(Truth.True, Eval("poe2", U("Or"), T()));

    [Fact]
    public void UnknownAndTrue_IsUnknown() => Assert.Equal(Truth.Unknown, Eval("poe2", U("And"), T()));

    [Fact]
    public void SameUnknownCallTwice_IsOneVariable()
        => Assert.Equal(Truth.True, Eval("poe2", U("Or"), U(not: true)));

    [Fact]
    public void DifferentUnknownCalls_AreIndependent()
        => Assert.Equal(Truth.Unknown, Eval("poe2", U("Or"), U2()));

    [Fact]
    public void BranchNot_IsIgnored()
    {
        var branch = new ConditionBranch([F()], Not: true, Operator: "And");
        Assert.Equal(Truth.False, Eval("poe2", branch));
    }

    [Fact]
    public void NestedBranch_EvaluatesRecursively()
    {
        var branch = new ConditionBranch([F("Or"), T()], Not: false, Operator: "And");
        Assert.Equal(Truth.True, Eval("poe2", T("And"), branch));
    }

    [Fact]
    public void UnrecognisedOperator_IsUnknown()
        => Assert.Equal(Truth.Unknown, Eval("poe2", T("Xor"), T()));

    [Theory]
    [InlineData("and", "or")]
    [InlineData("AND", "OR")]
    public void OperatorCaseInsensitive(string and, string or)
    {
        Assert.Equal(Truth.False, Eval("poe2", T(and), F()));
        Assert.Equal(Truth.True, Eval("poe2", T(or), F()));
    }

    [Fact]
    public void MoreThanTwelveUnknowns_IsUnknownWithoutEnumerating()
    {
        int calls = 0;
        var nodes = new List<ConditionNode> { T("And") };
        for (int i = 0; i < 13; i++)
            nodes.Add(Leaf($"V{i}()", i == 12 ? "And" : "Or"));
        var result = ConditionEvaluator.Evaluate(nodes, new GameState(), "poe2",
            (l, s) => { calls++; return l.FullName == "Boolean T()" ? Truth.True : Truth.Unknown; });
        Assert.Equal(Truth.Unknown, result);
        Assert.True(calls <= 14, $"leaf evaluator called {calls} times");
    }

    [Fact]
    public void Poe1GameId_UsesPoe1Fold()
    {
        Assert.Equal(Truth.True, Eval("poe1", F("Or"), T("And"), F()));
        Assert.Equal(Truth.False, Eval("poe2", F("Or"), T("And"), F()));
    }

    [Fact]
    public void UnknownGameId_UsesPoe2Fold()
        => Assert.Equal(Truth.False, Eval("", F("Or"), T("And"), F()));
}
