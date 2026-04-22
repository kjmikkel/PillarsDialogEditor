using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Layout;

public class AutoLayoutServiceTests
{
    private const double NodeWidth = 220;
    private const double NodeHeight = 110;
    private const double HGap = 60;
    private const double VGap = 20;

    private static ConversationNode Node(int id, params int[] toIds) =>
        new(id, false, SpeakerCategory.Npc, "", "", toIds.Select(t => new NodeLink(id, t, false)).ToList(),
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
        Assert.True(yDiff >= NodeHeight + VGap);
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
    public void Apply_AllNodesReceivePosition()
    {
        var nodes = new[] { Node(0, 1), Node(1, 2), Node(2) };
        var positions = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));

        Assert.Equal(3, positions.Count);
    }
}
