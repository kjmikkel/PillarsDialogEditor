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

    [AvaloniaFact]
    public void CanvasPeer_HasTheNodesAsChildren()
    {
        var (_, editor, root, child) = Setup();
        var children = ControlAutomationPeer.CreatePeerForElement(editor).GetChildren();
        Assert.Contains(PeerOf(editor, root), children);
        Assert.Contains(PeerOf(editor, child), children);
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
