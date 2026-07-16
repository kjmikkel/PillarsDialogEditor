using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Editing;

public class NodeConditionExtensionsTests
{
    private static ConditionLeaf Leaf(string full, params string[] args) =>
        new(full, args, Not: false, Operator: "And");

    private static NodeEditSnapshot Node(
        IReadOnlyList<ConditionNode> nodeConds,
        params IReadOnlyList<ConditionNode>?[] linkConds)
    {
        var links = linkConds.Select((c, i) =>
            new LinkEditSnapshot(0, i + 1, 0f, "", HasConditions: c is { Count: > 0 })
            { Conditions = c }).ToList();
        return new NodeEditSnapshot(
            0, false, SpeakerCategory.Npc, "", "", "", "", "", "", "", "", "", false, false,
            links, nodeConds, []);
    }

    [Fact]
    public void ConditionLeaves_YieldsNodeAndLinkLeaves_InOrder()
    {
        var node = Node(
            nodeConds: new ConditionNode[] { Leaf("Boolean A()") },
            new ConditionNode[] { Leaf("Boolean B()") },        // link 1
            null,                                                // link 2: no conditions
            new ConditionNode[] { Leaf("Boolean C()") });        // link 3

        var names = node.ConditionLeaves().Select(l => l.FullName).ToList();

        Assert.Equal(new[] { "Boolean A()", "Boolean B()", "Boolean C()" }, names);
    }

    [Fact]
    public void ConditionLeaves_FlattensBranches()
    {
        var branch = new ConditionBranch(
            new ConditionNode[] { Leaf("Boolean X()"), Leaf("Boolean Y()") },
            Not: false, Operator: "Or");
        var node = Node(nodeConds: new ConditionNode[] { branch });

        var names = node.ConditionLeaves().Select(l => l.FullName).ToList();

        Assert.Equal(new[] { "Boolean X()", "Boolean Y()" }, names);
    }
}
