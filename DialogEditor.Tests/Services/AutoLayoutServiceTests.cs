using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Services;

public class AutoLayoutServiceTests
{
    // ── Helpers ──────────────────────────────────────────────────────────

    private static ConversationNode Node(int id, params int[] targets)
        => new(id, false, SpeakerCategory.Npc, "", "",
               targets.Select(t => new NodeLink(id, t, [])).ToList(),
               [], [], "Conversation", "None");

    private static Dictionary<int, (double x, double y)> Capture(
        IReadOnlyList<ConversationNode> nodes)
    {
        var pos = new Dictionary<int, (double, double)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => pos[id] = (x, y));
        return pos;
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void Apply_EmptyList_NoCallbackInvoked()
    {
        var called = false;
        AutoLayoutService.Apply([], (_, _, _) => called = true);
        Assert.False(called);
    }

    [Fact]
    public void Apply_SingleNode_PlacedAtLayer0()
    {
        var pos = Capture([Node(0)]);
        Assert.Equal(0.0, pos[0].x);
    }

    [Fact]
    public void Apply_LinearChain_SuccessiveLayers()
    {
        // 0 → 1 → 2
        var pos = Capture([Node(0, 1), Node(1, 2), Node(2)]);
        Assert.True(pos[0].x < pos[1].x, "root should be left of middle");
        Assert.True(pos[1].x < pos[2].x, "middle should be left of leaf");
    }

    [Fact]
    public void Apply_BranchingTree_SiblingsSameLayer()
    {
        // 0 → 1 and 0 → 2
        var pos = Capture([Node(0, 1, 2), Node(1), Node(2)]);
        Assert.Equal(pos[1].x, pos[2].x);
        Assert.NotEqual(pos[1].y, pos[2].y);
        Assert.True(pos[0].x < pos[1].x);
    }

    [Fact]
    public void Apply_MultipleRoots_BothAtLayer0()
    {
        // Two disconnected trees: 0 → 1, and 2 → 3
        var pos = Capture([Node(0, 1), Node(1), Node(2, 3), Node(3)]);
        Assert.Equal(pos[0].x, pos[2].x);  // both roots at same x
        Assert.Equal(0.0, pos[0].x);
    }

    [Fact]
    public void Apply_Cycle_BothNodesGetPositions()
    {
        // A↔B: neither has zero incoming links, algorithm seeds from nodes[0]
        var nodes = new List<ConversationNode> { Node(0, 1), Node(1, 0) };
        var pos = Capture(nodes);
        Assert.True(pos.ContainsKey(0));
        Assert.True(pos.ContainsKey(1));
        // Nodes should be in different layers (not both at same position)
        Assert.False(pos[0].x == pos[1].x && pos[0].y == pos[1].y);
    }

    [Fact]
    public void Apply_TwoDisconnectedChains_RootsAtLayer0()
    {
        // Chain A: 0→1, Chain B: 10→11 — roots 0 and 10 should share x=0
        var pos = Capture([Node(0, 1), Node(1), Node(10, 11), Node(11)]);
        Assert.Equal(pos[0].x, pos[10].x);
        Assert.Equal(0.0, pos[0].x);
        Assert.Equal(pos[1].x, pos[11].x);
    }
}
