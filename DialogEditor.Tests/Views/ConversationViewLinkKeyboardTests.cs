using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Controls;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.Tests.ViewModels;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// Issue 361: canvas links must be reachable from the keyboard and by UI Automation. L / Shift+L
/// moves focus through the selected node's links, the Menu key then opens that link's menu,
/// and Escape returns focus to the node. Each link has an automation peer named after its ends.
public class ConversationViewLinkKeyboardTests
{
    private static (Window window, ConversationView view, ConversationViewModel vm, NodeViewModel root,
                    ConnectionViewModel first, ConnectionViewModel second) Setup()
    {
        Loc.Configure(new StubStringProvider());
        var vm = new ConversationViewModel(new StubDispatcher()) { IsEditable = true };
        var root = CanvasNavigationServiceTests.MakeNode(0, 0, 0);
        var a = CanvasNavigationServiceTests.MakeNode(1, 400, 0);
        var b = CanvasNavigationServiceTests.MakeNode(2, 400, 200);
        foreach (var n in new[] { root, a, b }) { n.OnSelected = x => vm.SelectedNode = x; vm.Nodes.Add(n); }
        var first = CanvasNavigationServiceTests.Connect(root, a);
        var second = CanvasNavigationServiceTests.Connect(root, b);
        vm.Connections.Add(first);
        vm.Connections.Add(second);

        var view = new ConversationView { DataContext = vm };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        view.FocusEditor();
        vm.SelectNode(root);
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm, root, first, second);
    }

    private static void Press(ConversationView view, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var target = (Control?)TopLevel.GetTopLevel(view)!.FocusManager!.GetFocusedElement()
                     ?? view.FindControl<Control>("Editor")!;
        // A whole keystroke: the menu keys act on key-up, as Avalonia's own context-menu handling does.
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
        target.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyUpEvent, Key = key, KeyModifiers = modifiers });
        Dispatcher.UIThread.RunJobs();
    }

    private static object? Focused(ConversationView view) =>
        TopLevel.GetTopLevel(view)!.FocusManager!.GetFocusedElement();

    private static CanvasConnection LinkControl(ConversationView view, ConnectionViewModel link) =>
        view.GetVisualDescendants().OfType<CanvasConnection>().Single(c => c.DataContext == link);

    [AvaloniaFact]
    public void L_FocusesTheSelectedNodesFirstLink_ShiftL_ThePrevious()
    {
        var (window, view, vm, _, first, second) = Setup();
        try
        {
            Press(view, Key.L);
            Assert.Same(first, vm.FocusedLink);
            Assert.Same(LinkControl(view, first), Focused(view));

            Press(view, Key.L);
            Assert.Same(LinkControl(view, second), Focused(view));

            Press(view, Key.L, KeyModifiers.Shift);
            Assert.Same(LinkControl(view, first), Focused(view));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void MenuKey_OnFocusedLink_OpensTheLinksMenu_NotTheNodes()
    {
        var (window, view, _, _, first, _) = Setup();
        try
        {
            Press(view, Key.L);
            Press(view, Key.Apps);

            Assert.True(LinkControl(view, first).ContextMenu!.IsOpen);
            var nodeMenus = view.GetVisualDescendants().OfType<Nodify.Node>()
                .Select(n => n.ContextMenu).Where(m => m is not null);
            Assert.DoesNotContain(nodeMenus, m => m!.IsOpen);
        }
        finally { window.Close(); }
    }

    // Real key input (press AND release), not a raised KeyDown: in the running app Shift+F10 on a
    // focused link left no menu open, while the raised-event test above passed.
    [AvaloniaTheory]
    [InlineData(Key.Apps, RawInputModifiers.None)]
    [InlineData(Key.F10, RawInputModifiers.Shift)]
    public void MenuKey_RealKeyboard_OnFocusedLink_LeavesTheLinksMenuOpen(Key key, RawInputModifiers modifiers)
    {
        var (window, view, _, _, first, _) = Setup();
        try
        {
            window.KeyPress(Key.L, RawInputModifiers.None, PhysicalKey.L, "l");
            window.KeyRelease(Key.L, RawInputModifiers.None, PhysicalKey.L, "l");
            Dispatcher.UIThread.RunJobs();
            Assert.Same(LinkControl(view, first), Focused(view));

            window.KeyPress(key, modifiers, PhysicalKey.None, null);
            window.KeyRelease(key, modifiers, PhysicalKey.None, null);
            Dispatcher.UIThread.RunJobs();

            Assert.True(LinkControl(view, first).ContextMenu!.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Escape_OnFocusedLink_ReturnsFocusToTheNode_KeepingSelection()
    {
        var (window, view, vm, root, _, _) = Setup();
        try
        {
            Press(view, Key.L);
            Press(view, Key.Escape);

            Assert.Null(vm.FocusedLink);
            Assert.Same(root, vm.SelectedNode);
            var editor = view.FindControl<Nodify.NodifyEditor>("Editor")!;
            Assert.Same(editor.ContainerFromItem(root), Focused(view));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Link_IsInTheAutomationTree_NamedAfterItsEnds()
    {
        var (window, view, _, _, first, _) = Setup();
        try
        {
            var peer = ControlAutomationPeer.CreatePeerForElement(LinkControl(view, first));

            Assert.True(peer.IsControlElement());
            Assert.Equal(first.AutomationName, peer.GetName());
            Assert.Contains(peer, Descendants(ControlAutomationPeer.CreatePeerForElement(window)));
        }
        finally { window.Close(); }
    }

    private static IEnumerable<AutomationPeer> Descendants(AutomationPeer peer)
    {
        foreach (var child in peer.GetChildren())
        {
            yield return child;
            foreach (var d in Descendants(child)) yield return d;
        }
    }
}
