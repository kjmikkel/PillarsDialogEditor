using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.Tests.ViewModels;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Nodify;

namespace DialogEditor.Tests.Views;

/// GitHub issue 311: each canvas node is a UIA list item, named like the node and selectable
/// through the SelectionItem pattern. Nodify's ItemContainer has no peer of its own, so it
/// dropped out of the tree: UIA hit-testing then landed on the whole canvas and scripts could
/// neither click nor select a node.
public class CanvasNodeAutomationTests
{
    private static (ConversationViewModel vm, NodifyEditor editor, NodeViewModel root, NodeViewModel child) Setup()
    {
        Loc.Configure(new StubStringProvider());
        var vm = new ConversationViewModel(new StubDispatcher()) { IsEditable = true };
        var root  = CanvasNavigationServiceTests.MakeNode(0, 0, 0);
        var child = CanvasNavigationServiceTests.MakeNode(1, 400, 0);
        root.OnSelected  = n => vm.SelectedNode = n;
        child.OnSelected = n => vm.SelectedNode = n;
        vm.Nodes.Add(root);
        vm.Nodes.Add(child);
        vm.Connections.Add(CanvasNavigationServiceTests.Connect(root, child));

        var view = new ConversationView { DataContext = vm };
        var window = new Window { Content = view, Width = 1200, Height = 800 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, view.FindControl<NodifyEditor>("Editor")!, root, child);
    }

    private static AutomationPeer PeerOf(NodifyEditor editor, NodeViewModel node)
        => ControlAutomationPeer.CreatePeerForElement((Control)editor.ContainerFromItem(node)!);

    [AvaloniaFact]
    public void NodeContainer_IsANamedListItem()
    {
        var (_, editor, root, _) = Setup();
        var peer = PeerOf(editor, root);
        Assert.Equal(AutomationControlType.ListItem, peer.GetAutomationControlType());
        Assert.True(peer.IsControlElement());
        Assert.Equal(root.AutomationName, peer.GetName());
    }

    // The Win32 UIA bridge (Avalonia.Win32.Automation.AutomationNode.Navigate) walks the tree
    // as FirstChild = GetChildren()[0] and NextSibling = the next entry in GetParent()'s
    // GetChildren(). A peer's parent is whichever ancestor last listed it, and
    // ControlAutomationPeer.EnsureConnected asks every visual ancestor in turn, so a peer that
    // lists children it doesn't own (e.g. flattening Nodify's peer-less layers) leaves those
    // children pointing at a parent that doesn't list them: the walk then never reaches the
    // nodes. These tests navigate exactly the way the bridge does.
    private static AutomationPeer? NextSibling(AutomationPeer peer)
    {
        var siblings = peer.GetParent()?.GetChildren();
        if (siblings is null) return null;
        for (var i = 0; i < siblings.Count - 1; i++)
            if (siblings[i] == peer) return siblings[i + 1];
        return null;
    }

    private static List<AutomationPeer> WalkLikeUia(AutomationPeer root)
    {
        var found = new List<AutomationPeer>();
        void Visit(AutomationPeer p)
        {
            found.Add(p);
            for (var c = p.GetChildren().FirstOrDefault(); c is not null; c = NextSibling(c))
                Visit(c);
        }
        Visit(root);
        return found;
    }

    // In the app a UIA client has usually built the peers before a conversation is opened
    // (or a node is added). Then only the peer that owns the new visual children is
    // invalidated, so the test adds a node after a first walk, as the GUI run did.
    private static NodeViewModel AddNodeAfterAWalk(ConversationViewModel vm, NodifyEditor editor)
    {
        WalkLikeUia(ControlAutomationPeer.CreatePeerForElement(TopLevel.GetTopLevel(editor)!));
        var late = CanvasNavigationServiceTests.MakeNode(2, 800, 0);
        vm.Nodes.Add(late);
        Dispatcher.UIThread.RunJobs();
        return late;
    }

    [AvaloniaFact]
    public void UiaWalk_FromTheWindow_ReachesEveryNode()
    {
        var (vm, editor, root, child) = Setup();
        var late = AddNodeAfterAWalk(vm, editor);
        var all = WalkLikeUia(ControlAutomationPeer.CreatePeerForElement(TopLevel.GetTopLevel(editor)!));
        Assert.Contains(PeerOf(editor, root), all);
        Assert.Contains(PeerOf(editor, child), all);
        Assert.Contains(PeerOf(editor, late), all);
    }

    [AvaloniaFact]
    public void NodeParentChain_IsConsistent_UpToTheCanvas()
    {
        var (vm, editor, _, _) = Setup();
        var late = AddNodeAfterAWalk(vm, editor);
        var editorPeer = ControlAutomationPeer.CreatePeerForElement(editor);
        var peer = PeerOf(editor, late);
        for (var i = 0; peer != editorPeer; i++)
        {
            Assert.True(i < 20, "the node's parent chain never reaches the canvas");
            var parent = peer.GetParent();
            Assert.NotNull(parent);
            Assert.Contains(peer, parent!.GetChildren());
            peer = parent;
        }
    }

    [AvaloniaFact]
    public void IsSelected_FollowsTheCanvasSelection()
    {
        var (vm, editor, root, child) = Setup();
        vm.SelectNode(root);
        Dispatcher.UIThread.RunJobs();
        Assert.True(PeerOf(editor, root).GetProvider<ISelectionItemProvider>()!.IsSelected);
        Assert.False(PeerOf(editor, child).GetProvider<ISelectionItemProvider>()!.IsSelected);
    }

    [AvaloniaFact]
    public void Select_SelectsThatNodeOnly()
    {
        // What a script (or a screen reader's "select") does instead of clicking the canvas.
        var (vm, editor, root, child) = Setup();
        vm.SelectNode(root);
        Dispatcher.UIThread.RunJobs();

        PeerOf(editor, child).GetProvider<ISelectionItemProvider>()!.Select();
        Dispatcher.UIThread.RunJobs();

        Assert.Same(child, vm.SelectedNode);
        Assert.True(child.IsSelected);
        Assert.False(root.IsSelected);
    }

}
