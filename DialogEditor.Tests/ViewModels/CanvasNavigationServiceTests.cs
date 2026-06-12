using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class CanvasNavigationServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────
    internal static NodeViewModel MakeNode(int id, double x = 0, double y = 0)
    {
        var n = new NodeViewModel(
            new ConversationNode(id, false, SpeakerCategory.Npc,
                string.Empty, string.Empty, [], [], [], "Conversation", "None"),
            new StringEntry(id, string.Empty, string.Empty))
        { Location = new LayoutPoint(x, y) };
        // Owner is internal; DialogEditor.ViewModels has InternalsVisibleTo(DialogEditor.Tests)
        n.Input.Owner  = n;
        n.Output.Owner = n;
        return n;
    }

    internal static ConnectionViewModel Connect(NodeViewModel parent, NodeViewModel child) =>
        new(parent.Output, child.Input);

    // ── Child ─────────────────────────────────────────────────────────────
    [Fact]
    public void GetChild_SingleChild_ReturnsIt()
    {
        var a = MakeNode(0); var b = MakeNode(1, 400, 0);
        var nodes = new[] { a, b };
        var conns = new[] { Connect(a, b) };
        Assert.Same(b, CanvasNavigationService.GetChild(a, nodes, conns));
    }

    [Fact]
    public void GetChild_MultipleChildren_PicksVerticallyNearest()
    {
        var a = MakeNode(0, 0, 100);
        var far  = MakeNode(1, 400, 300);
        var near = MakeNode(2, 400, 120);
        var nodes = new[] { a, far, near };
        var conns = new[] { Connect(a, far), Connect(a, near) };
        Assert.Same(near, CanvasNavigationService.GetChild(a, nodes, conns));
    }

    [Fact]
    public void GetChild_TieOnDistance_PicksFirstLinkOrder()
    {
        var a = MakeNode(0, 0, 100);
        var up   = MakeNode(1, 400, 50);   // |50-100|  = 50
        var down = MakeNode(2, 400, 150);  // |150-100| = 50 — tie
        var nodes = new[] { a, up, down };
        var conns = new[] { Connect(a, up), Connect(a, down) };
        Assert.Same(up, CanvasNavigationService.GetChild(a, nodes, conns)); // first connection wins
    }

    [Fact]
    public void GetChild_NoChildren_ReturnsNull()
    {
        var a = MakeNode(0);
        Assert.Null(CanvasNavigationService.GetChild(a, new[] { a }, []));
    }

    [Fact]
    public void GetChild_SelfLoop_IsIgnored()
    {
        var a = MakeNode(0);
        var conns = new[] { Connect(a, a) };
        Assert.Null(CanvasNavigationService.GetChild(a, new[] { a }, conns));
    }

    // ── Parent ────────────────────────────────────────────────────────────
    [Fact]
    public void GetParent_SingleParent_ReturnsIt()
    {
        var a = MakeNode(0); var b = MakeNode(1, 400, 0);
        var nodes = new[] { a, b };
        var conns = new[] { Connect(a, b) };
        Assert.Same(a, CanvasNavigationService.GetParent(b, nodes, conns));
    }

    [Fact]
    public void GetParent_MultipleParents_PicksVerticallyNearest()
    {
        var child = MakeNode(2, 400, 100);
        var far  = MakeNode(0, 0, 400);
        var near = MakeNode(1, 0, 90);
        var nodes = new[] { far, near, child };
        var conns = new[] { Connect(far, child), Connect(near, child) };
        Assert.Same(near, CanvasNavigationService.GetParent(child, nodes, conns));
    }

    [Fact]
    public void GetParent_Root_ReturnsNull()
    {
        var a = MakeNode(0); var b = MakeNode(1);
        var conns = new[] { Connect(a, b) };
        Assert.Null(CanvasNavigationService.GetParent(a, new[] { a, b }, conns));
    }
}
