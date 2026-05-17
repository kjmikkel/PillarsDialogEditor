using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Models;

public class ConditionNodeTests
{
    // ── ConditionLeaf.Format ──────────────────────────────────────────────

    [Fact]
    public void Leaf_Format_StripsPrefixAndShowsParams()
    {
        var leaf = new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)",
            ["flag_a", "EqualTo", "1"], Not: false, Operator: "And");
        Assert.Equal("IsGlobalValue(flag_a, EqualTo, 1)", leaf.Format());
    }

    [Fact]
    public void Leaf_Format_Not_PrependNOT()
    {
        var leaf = new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)",
            ["flag_a", "EqualTo", "1"], Not: true, Operator: "And");
        Assert.StartsWith("NOT ", leaf.Format());
    }

    [Fact]
    public void Leaf_Format_NoParams_ShowsEmptyParens()
    {
        var leaf = new ConditionLeaf("Boolean IsInCombat()",
            [], Not: false, Operator: "And");
        Assert.Equal("IsInCombat()", leaf.Format());
    }

    // ── ConditionBranch.Format ────────────────────────────────────────────

    [Fact]
    public void Branch_Format_WrapsChildren()
    {
        var inner = new ConditionLeaf("Boolean A()", [], false, "Or");
        var branch = new ConditionBranch([inner], Not: false, Operator: "And");
        Assert.Contains("(", branch.Format());
        Assert.Contains(")", branch.Format());
    }

    // ── FormatTree ────────────────────────────────────────────────────────

    [Fact]
    public void FormatTree_EmptyList_ReturnsEmpty()
    {
        IReadOnlyList<ConditionNode> empty = [];
        Assert.Equal(string.Empty, empty.FormatTree());
    }

    [Fact]
    public void FormatTree_TwoLeaves_JoinsWithOperator()
    {
        IReadOnlyList<ConditionNode> nodes =
        [
            new ConditionLeaf("Boolean A()", [], false, "And"),
            new ConditionLeaf("Boolean B()", [], false, "And"),
        ];
        var result = nodes.FormatTree();
        Assert.Contains("AND", result);
        Assert.Contains("A()", result);
        Assert.Contains("B()", result);
    }

    // ── Leaves ────────────────────────────────────────────────────────────

    [Fact]
    public void Leaves_FlattensBranchChildren()
    {
        var leaf1 = new ConditionLeaf("Boolean A()", [], false, "And");
        var leaf2 = new ConditionLeaf("Boolean B()", [], false, "Or");
        var branch = new ConditionBranch([leaf1, leaf2], false, "And");
        Assert.Equal(2, branch.Leaves().Count());
    }
}
