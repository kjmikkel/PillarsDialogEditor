using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// A link's position number rides on its own curve, so links from one port do not stack (issue 359).
public class LinkLabelPointTests
{
    public LinkLabelPointTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel MakeNode(int id)
    {
        var node = new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None");
        var vm = new NodeViewModel(node, new StringEntry(id, "text", ""));
        vm.Input.Owner = vm;
        vm.Output.Owner = vm;
        return vm;
    }

    [Fact]
    public void LabelPoint_IsTheBezierPointNearTheSource_PlusTheOffset()
    {
        // Worked by hand from Nodify's Connection (Spacing 20, static offset 14): source end (14,0),
        // target end (386,200), bezier (34,0) (220,0) (180,200) (366,200), t = 0.3, then the label offset.
        var p = LinkLabelGeometry.LabelPoint(new LayoutPoint(0, 0), new LayoutPoint(400, 200));

        Assert.Equal(152.584 + LinkLabelGeometry.LabelOffset.X, p.X, 2);
        Assert.Equal(43.2 + LinkLabelGeometry.LabelOffset.Y, p.Y, 2);
    }

    [Fact]
    public void LinksFromOnePort_GetLabelPointsAtLeastTenPixelsApart()
    {
        var canvas = new ConversationViewModel(new StubDispatcher());
        var src = MakeNode(0); var a = MakeNode(1); var b = MakeNode(2);
        foreach (var n in new[] { src, a, b }) canvas.Nodes.Add(n);
        src.Output.Anchor = new LayoutPoint(300, 100);
        a.Input.Anchor = new LayoutPoint(700, 100);
        b.Input.Anchor = new LayoutPoint(700, 300);
        var one = new ConnectionViewModel(src.Output, a.Input);
        var two = new ConnectionViewModel(src.Output, b.Input);
        canvas.Connections.Add(one);
        canvas.Connections.Add(two);

        var d = one.LinkPositionLabelPoint;
        var e = two.LinkPositionLabelPoint;
        Assert.True(Math.Abs(d.Y - e.Y) >= 10);
    }

    [Fact]
    public void MovingEitherEnd_RaisesTheChange_AndMovesThePoint()
    {
        var canvas = new ConversationViewModel(new StubDispatcher());
        var src = MakeNode(0); var a = MakeNode(1);
        canvas.Nodes.Add(src); canvas.Nodes.Add(a);
        src.Output.Anchor = new LayoutPoint(300, 100);
        a.Input.Anchor = new LayoutPoint(700, 100);
        var link = new ConnectionViewModel(src.Output, a.Input);
        canvas.Connections.Add(link);
        var raised = 0;
        link.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ConnectionViewModel.LinkPositionLabelPoint)) raised++; };
        var before = link.LinkPositionLabelPoint;

        a.Input.Anchor = new LayoutPoint(700, 400);
        Assert.Equal(1, raised);
        Assert.NotEqual(before, link.LinkPositionLabelPoint);

        src.Output.Anchor = new LayoutPoint(300, 150);
        Assert.Equal(2, raised);
    }

    [Fact]
    public void RemovedLink_StopsListeningToTheAnchors()
    {
        var canvas = new ConversationViewModel(new StubDispatcher());
        var src = MakeNode(0); var a = MakeNode(1);
        canvas.Nodes.Add(src); canvas.Nodes.Add(a);
        var link = new ConnectionViewModel(src.Output, a.Input);
        canvas.Connections.Add(link);
        canvas.Connections.Remove(link);
        var raised = 0;
        link.PropertyChanged += (_, _) => raised++;

        a.Input.Anchor = new LayoutPoint(10, 10);

        Assert.Equal(0, raised);
    }
}
