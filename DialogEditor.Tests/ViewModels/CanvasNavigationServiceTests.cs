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

    // ── Siblings ──────────────────────────────────────────────────────────
    [Fact]
    public void GetSibling_NextAndPrevious_InVisualOrder()
    {
        var p  = MakeNode(0, 0, 100);
        var s1 = MakeNode(1, 400, 50);
        var s2 = MakeNode(2, 400, 150);
        var s3 = MakeNode(3, 400, 250);
        var nodes = new[] { p, s1, s2, s3 };
        var conns = new[] { Connect(p, s1), Connect(p, s2), Connect(p, s3) };
        Assert.Same(s3, CanvasNavigationService.GetSibling(s2, +1, nodes, conns));
        Assert.Same(s1, CanvasNavigationService.GetSibling(s2, -1, nodes, conns));
    }

    [Fact]
    public void GetSibling_AtEnds_DoesNotWrap()
    {
        var p  = MakeNode(0, 0, 100);
        var s1 = MakeNode(1, 400, 50);
        var s2 = MakeNode(2, 400, 150);
        var nodes = new[] { p, s1, s2 };
        var conns = new[] { Connect(p, s1), Connect(p, s2) };
        Assert.Null(CanvasNavigationService.GetSibling(s1, -1, nodes, conns));
        Assert.Null(CanvasNavigationService.GetSibling(s2, +1, nodes, conns));
    }

    [Fact]
    public void GetSibling_ParentlessNodes_FormOneGroup()
    {
        // Roots and orphans are each other's siblings, ordered by Y.
        var root   = MakeNode(0, 0, 0);
        var orphan = MakeNode(5, 800, 200);
        var child  = MakeNode(1, 400, 0);
        var nodes = new[] { root, orphan, child };
        var conns = new[] { Connect(root, child) };
        Assert.Same(orphan, CanvasNavigationService.GetSibling(root, +1, nodes, conns));
        Assert.Same(root,   CanvasNavigationService.GetSibling(orphan, -1, nodes, conns));
    }

    [Fact]
    public void GetSibling_OnlyChild_ReturnsNull()
    {
        var p = MakeNode(0); var c = MakeNode(1, 400, 0);
        var nodes = new[] { p, c };
        var conns = new[] { Connect(p, c) };
        Assert.Null(CanvasNavigationService.GetSibling(c, +1, nodes, conns));
        Assert.Null(CanvasNavigationService.GetSibling(c, -1, nodes, conns));
    }

    // ── Cycle ─────────────────────────────────────────────────────────────
    [Fact]
    public void Cycle_Forward_FollowsCollectionOrderAndWraps()
    {
        var a = MakeNode(0); var b = MakeNode(1); var c = MakeNode(2);
        var nodes = new[] { a, b, c };
        Assert.Same(b, CanvasNavigationService.Cycle(a, forward: true, nodes));
        Assert.Same(a, CanvasNavigationService.Cycle(c, forward: true, nodes)); // wraps
    }

    [Fact]
    public void Cycle_Backward_Wraps()
    {
        var a = MakeNode(0); var b = MakeNode(1);
        var nodes = new[] { a, b };
        Assert.Same(b, CanvasNavigationService.Cycle(a, forward: false, nodes)); // wraps
    }

    [Fact]
    public void Cycle_FromNull_EntersAtFirstOrLast()
    {
        var a = MakeNode(0); var b = MakeNode(1);
        var nodes = new[] { a, b };
        Assert.Same(a, CanvasNavigationService.Cycle(null, forward: true, nodes));
        Assert.Same(b, CanvasNavigationService.Cycle(null, forward: false, nodes));
    }

    [Fact]
    public void Cycle_ReachesOrphans()
    {
        var root = MakeNode(0); var orphan = MakeNode(7);
        var nodes = new[] { root, orphan };
        Assert.Same(orphan, CanvasNavigationService.Cycle(root, forward: true, nodes));
    }

    [Fact]
    public void Cycle_EmptyCanvas_ReturnsNull()
    {
        Assert.Null(CanvasNavigationService.Cycle(null, forward: true, []));
    }
}
