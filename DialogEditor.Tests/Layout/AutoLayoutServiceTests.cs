using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Layout;

public class AutoLayoutServiceTests
{
    private const double Width  = CardMetrics.Width;
    private const double RowGap = CardMetrics.RowGap;

    private static Dictionary<int, (double X, double Y)> Layout(
        IReadOnlyList<ConversationNode> nodes, Func<int, double> heightOf)
    {
        var p = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, heightOf, (id, x, y) => p[id] = (x, y));
        return p;
    }

    [Fact]
    public void Apply_ColumnPitch_IsCardWidthPlusColumnGap()
    {
        var p = Layout(new[] { Node(0, 1), Node(1) }, _ => 50);
        Assert.Equal(CardMetrics.Width + CardMetrics.ColumnGap, p[1].X - p[0].X);
    }

    [Fact]
    public void Apply_Siblings_SitRowGapBelowThePreviousCardsBottom()
    {
        // Node 1 is tall, node 2 short: node 2 starts RowGap below node 1's bottom edge,
        // not one fixed pitch below its top.
        var heights = new Dictionary<int, double> { [0] = 40, [1] = 113, [2] = 40 };
        var p = Layout(new[] { Node(0, 1, 2), Node(1), Node(2) }, id => heights[id]);
        var (upper, lower) = p[1].Y < p[2].Y ? (1, 2) : (2, 1);
        Assert.Equal(p[upper].Y + heights[upper] + RowGap, p[lower].Y, 3);
    }

    [Fact]
    public void Apply_NoTwoCardsOverlap_ForMixedHeights()
    {
        // A branching conversation with several roots and a back-edge.
        var nodes = new[] { Node(0, 1, 2, 3), Node(1, 4), Node(2, 4, 5), Node(3), Node(4, 0), Node(5), Node(6, 5) };
        var rnd = new Random(82);
        var heights = nodes.ToDictionary(n => n.NodeId, _ => 41 + rnd.Next(0, 73));
        var p = Layout(nodes, id => heights[id]);
        foreach (var a in nodes)
            foreach (var b in nodes.Where(b => b.NodeId > a.NodeId))
            {
                var (pa, pb) = (p[a.NodeId], p[b.NodeId]);
                var overlapX = pa.X < pb.X + Width && pb.X < pa.X + Width;
                var overlapY = pa.Y < pb.Y + heights[b.NodeId] && pb.Y < pa.Y + heights[a.NodeId];
                Assert.False(overlapX && overlapY, $"nodes {a.NodeId} and {b.NodeId} overlap");
            }
    }

    [Fact]
    public void Apply_TwoArgumentOverload_UsesMaxHeight()
    {
        var p = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(new[] { Node(0, 1, 2), Node(1), Node(2) }, (id, x, y) => p[id] = (x, y));
        Assert.Equal(CardMetrics.MaxHeight + RowGap, Math.Abs(p[1].Y - p[2].Y), 3);
    }

    private static ConversationNode Node(int id, params int[] toIds) =>
        new(id, false, SpeakerCategory.Npc, "", "", toIds.Select(t => new NodeLink(id, t, [])).ToList(),
            [], [], "", "");

    [Fact]
    public void Apply_SingleNode_PlacedAtOrigin()
    {
        var nodes = new[] { Node(0) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        Assert.Equal(0, positions[0].X);
        Assert.Equal(0, positions[0].Y);
    }

    [Fact]
    public void Apply_TwoConnectedNodes_SecondIsRightOfFirst()
    {
        var nodes = new[] { Node(0, 1), Node(1) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        Assert.Equal(0, positions[0].X);
        Assert.True(positions[1].X > positions[0].X);
    }

    [Fact]
    public void Apply_TwoNodesInSameLayer_VerticallySpaced()
    {
        var nodes = new[] { Node(0, 1, 2), Node(1), Node(2) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        var yDiff = Math.Abs(positions[1].Y - positions[2].Y);
        Assert.True(yDiff >= CardMetrics.MaxHeight + RowGap);
    }

    [Fact]
    public void Apply_ThreeNodeChain_XPositionsIncreaseMonotonically()
    {
        var nodes = new[] { Node(0, 1), Node(1, 2), Node(2) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        Assert.True(positions[1].X > positions[0].X);
        Assert.True(positions[2].X > positions[1].X);
    }

    [Fact]
    public void Apply_SingleParentChild_AlignsWithParentRowInsteadOfBeingBumped()
    {
        // Three roots so the parent (0) sits at the top row, not centred on the origin.
        // 0 → 4 and 0 → 5 (each has only parent 0); 4 → 6 (only parent 4).
        var nodes = new[]
        {
            Node(0, 4, 5), Node(1), Node(2), Node(4, 6), Node(5), Node(6),
        };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        // The topmost child inherits its only parent's row instead of being bumped down.
        Assert.Equal(positions[0].Y, positions[4].Y);
        // The grandchild likewise aligns with its only parent.
        Assert.Equal(positions[4].Y, positions[6].Y);
        // Only the genuinely-conflicting second child drops, by exactly one row.
        Assert.Equal(positions[4].Y + (CardMetrics.MaxHeight + RowGap), positions[5].Y);
    }

    [Fact]
    public void Apply_AllNodesReceivePosition()
    {
        var nodes = new[] { Node(0, 1), Node(1, 2), Node(2) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        Assert.Equal(3, positions.Count);
    }
}
