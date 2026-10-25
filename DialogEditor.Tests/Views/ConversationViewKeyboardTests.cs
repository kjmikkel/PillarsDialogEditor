using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.Tests.ViewModels;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Nodify;

namespace DialogEditor.Tests.Views;

public class ConversationViewKeyboardTests
{
    private static (Window window, ConversationView view, ConversationViewModel vm,
                    NodeViewModel root, NodeViewModel child) Setup()
    {
        var vm = new ConversationViewModel(new StubDispatcher()) { IsEditable = true };
        var root  = CanvasNavigationServiceTests.MakeNode(0, 0, 0);
        var child = CanvasNavigationServiceTests.MakeNode(1, 400, 0);
        root.OnSelected  = n => vm.SelectedNode = n;
        child.OnSelected = n => vm.SelectedNode = n;
        vm.Nodes.Add(root);
        vm.Nodes.Add(child);
        vm.Connections.Add(CanvasNavigationServiceTests.Connect(root, child));

        var view = new ConversationView { DataContext = vm };
        var window = new Window { Content = view };
        window.Show();
        return (window, view, vm, root, child);
    }

    private static void Press(ConversationView view, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        var editor = view.FindControl<Control>("Editor")!;
        editor.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = key,
            KeyModifiers = modifiers,
        });
    }

    [AvaloniaFact]
    public void ArrowRight_SelectsChild()
    {
        var (_, view, vm, root, child) = Setup();
        vm.SelectNode(root);
        Press(view, Key.Right);
        Assert.Same(child, vm.SelectedNode);
    }

    [AvaloniaFact]
    public void ArrowLeft_SelectsParent()
    {
        var (_, view, vm, root, child) = Setup();
        vm.SelectNode(child);
        Press(view, Key.Left);
        Assert.Same(root, vm.SelectedNode);
    }

    [AvaloniaFact]
    public void CtrlArrow_NudgesSmallStep_CtrlShiftArrow_NudgesLargeStep()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);
        Press(view, Key.Right, KeyModifiers.Control);
        Assert.Equal(new LayoutPoint(10, 0), root.Location);
        Press(view, Key.Down, KeyModifiers.Control | KeyModifiers.Shift);
        Assert.Equal(new LayoutPoint(10, 50), root.Location);
    }

    [AvaloniaFact]
    public void PageDown_CyclesAllNodes()
    {
        var (_, view, vm, root, child) = Setup();
        Press(view, Key.PageDown);
        Assert.Same(root, vm.SelectedNode);
        Press(view, Key.PageDown);
        Assert.Same(child, vm.SelectedNode);
    }

    [AvaloniaFact]
    public void Home_SelectsRoot()
    {
        var (_, view, vm, root, child) = Setup();
        vm.SelectNode(child);
        Press(view, Key.Home);
        Assert.Same(root, vm.SelectedNode);
    }

    [AvaloniaFact]
    public void Escape_Deselects()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);
        Press(view, Key.Escape);
        Assert.Null(vm.SelectedNode);
    }

    [AvaloniaFact]
    public void Enter_RaisesFocusDetailRequested()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);
        var raised = false;
        view.FocusDetailRequested += (_, _) => raised = true;
        Press(view, Key.Enter);
        Assert.True(raised);
    }

    [AvaloniaFact]
    public void Enter_WithoutSelection_DoesNotRaise()
    {
        var (_, view, vm, _, _) = Setup();
        var raised = false;
        view.FocusDetailRequested += (_, _) => raised = true;
        Press(view, Key.Enter);
        Assert.False(raised);
    }

    [AvaloniaFact]
    public void TypingInSearchBox_DoesNotNavigate()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);
        // Raise the key on the SearchBox, not the editor: the editor handler
        // must not be attached anywhere that catches toolbar input.
        var searchBox = view.FindControl<TextBox>("SearchBox")!;
        searchBox.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Right,
        });
        Assert.Same(root, vm.SelectedNode); // unchanged
    }

    [AvaloniaFact]
    public void TabFocus_RestoresSelection()
    {
        var (_, view, vm, root, child) = Setup();
        vm.SelectNode(child);
        vm.Deselect();

        var editor = view.FindControl<Control>("Editor")!;
        editor.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });
        Assert.Same(child, vm.SelectedNode);
    }

    // Issue 238: ports were unnamed, invisible tab stops — two per node, so Tab from the
    // search box crossed every port in the conversation before leaving the canvas. The
    // canvas itself is the one stop; arrow keys move between nodes from there.
    [AvaloniaFact]
    public void Tab_FromSearchBox_SkipsConnectorPorts_AndReachesCanvas()
    {
        var (_, view, _, _, _) = Setup();
        var search = view.FindControl<TextBox>("SearchBox")!;
        var editor = view.FindControl<Control>("Editor")!;

        var stops = new List<IInputElement>();
        IInputElement? current = search;
        for (var i = 0; i < 50; i++)
        {
            current = KeyboardNavigationHandler.GetNext(current!, NavigationDirection.Next);
            if (current is null || ReferenceEquals(current, search) || stops.Contains(current)) break;
            stops.Add(current);
        }

        var chain = string.Join(" → ", stops.Select(s => $"{s.GetType().Name}({(s as ContentControl)?.Content})"));
        Assert.False(stops.Any(s => s is Connector), $"Tab chain enters a port: {chain}");
        Assert.True(stops.Contains(editor), $"Tab chain never reaches the canvas: {chain}");
    }

    [AvaloniaFact]
    public void MenuKey_OpensSelectedNodeContextMenu()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);

        Press(view, Key.Apps);

        // The node template's ContextMenu (Delete node / Add connected node)
        // must be open. Find it via the realized container.
        var editor = view.FindControl<Control>("Editor")!;
        var menu = ((global::Avalonia.Visual)editor).GetVisualDescendants()
            .OfType<Control>()
            .Select(c => c.ContextMenu)
            .FirstOrDefault(m => m is not null);
        Assert.NotNull(menu);
        Assert.True(menu!.IsOpen);
    }

    [AvaloniaFact]
    public void NodeDetailView_FocusFirstField_FocusesDefaultTextBox()
    {
        Loc.Configure(new StubStringProvider());
        var detail = new NodeDetailView();
        var window = new Window { Content = detail };
        window.Show();

        detail.FocusFirstField();

        var box = detail.FindControl<TextBox>("DefaultTextBox")!;
        Assert.True(box.IsFocused);
    }

    [AvaloniaFact]
    public void CtrlL_StartsConnectMode()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);

        Press(view, Key.L, KeyModifiers.Control);

        Assert.True(vm.IsConnecting);
        Assert.Same(root, vm.ConnectionSource);
    }

    [AvaloniaFact]
    public void CtrlL_WithoutSelection_DoesNotStartConnectMode()
    {
        var (_, view, vm, _, _) = Setup();

        Press(view, Key.L, KeyModifiers.Control);

        Assert.False(vm.IsConnecting);
    }

    [AvaloniaFact]
    public void Enter_WhileConnecting_ConfirmsInsteadOfFocusingDetail()
    {
        var (_, view, vm, root, _) = Setup();
        var target = CanvasNavigationServiceTests.MakeNode(2, 800, 0);
        target.OnSelected = n => vm.SelectedNode = n;
        vm.Nodes.Add(target);

        vm.BeginConnect(root);
        vm.SelectNode(target);

        var raised = false;
        view.FocusDetailRequested += (_, _) => raised = true;
        Press(view, Key.Enter);

        Assert.False(raised);
        Assert.False(vm.IsConnecting);
        Assert.Contains(vm.Connections, c => c.Source == root.Output && c.Target == target.Input);
    }

    [AvaloniaFact]
    public void Escape_WhileConnecting_CancelsInsteadOfDeselecting()
    {
        var (_, view, vm, root, _) = Setup();
        vm.BeginConnect(root);

        Press(view, Key.Escape);

        Assert.False(vm.IsConnecting);
        Assert.Same(root, vm.SelectedNode); // selection unchanged, per spec decision 2
    }

    [AvaloniaFact]
    public void ConnectToNode_MenuItem_StartsConnectMode()
    {
        var (_, view, vm, root, _) = Setup();
        vm.SelectNode(root);

        Press(view, Key.Apps);

        var editor = view.FindControl<Control>("Editor")!;
        var menu = ((global::Avalonia.Visual)editor).GetVisualDescendants()
            .OfType<Control>()
            .Select(c => c.ContextMenu)
            .FirstOrDefault(m => m is not null);
        Assert.NotNull(menu);

        var connectHeader = (string)Application.Current!.FindResource("Menu_ConnectToNode")!;
        var connectItem = menu!.Items
            .OfType<MenuItem>()
            .First(i => Equals(i.Header, connectHeader));
        connectItem.Command!.Execute(root);

        Assert.True(vm.IsConnecting);
        Assert.Same(root, vm.ConnectionSource);
    }

    [AvaloniaFact]
    public void ConnectionSourceOverlay_VisibleOnlyForConnectionSource()
    {
        var (_, view, vm, root, child) = Setup();
        vm.BeginConnect(root);

        var editor = view.FindControl<Control>("Editor")!;
        var overlays = ((global::Avalonia.Visual)editor).GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Name == "ConnectionSourceBadge")
            .ToList();

        Assert.Equal(2, overlays.Count); // one per realized node container
        Assert.True(root.IsConnectionSource);
        Assert.False(child.IsConnectionSource);
    }

    // ── Screen readers follow keyboard selection (GitHub issue 242) ──────
    // Focus moves onto the selected node's container, which is named after the node, so a
    // screen reader announces each keyboard move. Focus on the bare editor said nothing.

    private static IInputElement? Focused(Window window) => window.FocusManager?.GetFocusedElement();

    [AvaloniaFact]
    public void ArrowKey_MovesFocusToSelectedNode_WhichIsNamedAfterIt()
    {
        Loc.Configure(new StubStringProvider());   // AutomationName reads the speaker label
        var (window, view, vm, root, child) = Setup();
        view.FocusEditor();
        vm.SelectNode(root);

        Press(view, Key.Right);

        var editor    = view.FindControl<NodifyEditor>("Editor")!;
        var container = editor.ContainerFromItem(child);
        Assert.NotNull(container);
        Assert.Same(container, Focused(window));
        Assert.Equal(child.AutomationName,
                     global::Avalonia.Automation.AutomationProperties.GetName(container!));
    }

    [AvaloniaFact]
    public void ArrowKeys_StillNavigate_WhenSentFromTheFocusedNode()
    {
        Loc.Configure(new StubStringProvider());
        var (window, view, vm, root, child) = Setup();
        view.FocusEditor();
        vm.SelectNode(root);
        Press(view, Key.Home);   // focus is now on root's container

        // Real key input starts at the focused element, not at the editor.
        foreach (var key in new[] { Key.Right, Key.Left, Key.Right })
            ((Control)Focused(window)!).RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key });

        Assert.Same(child, vm.SelectedNode);
        Assert.Same(view.FindControl<NodifyEditor>("Editor")!.ContainerFromItem(child), Focused(window));
    }

    [AvaloniaFact]
    public void Escape_ReturnsFocusToCanvas()
    {
        var (window, view, vm, root, _) = Setup();
        view.FocusEditor();
        vm.SelectNode(root);
        Press(view, Key.Home);

        Press(view, Key.Escape);

        Assert.Null(vm.SelectedNode);
        Assert.Same(view.FindControl<Control>("Editor"), Focused(window));
    }

    [AvaloniaFact]
    public void Tab_FromFocusedNode_LeavesTheCanvas()
    {
        var (_, view, vm, root, _) = Setup();
        view.FocusEditor();
        vm.SelectNode(root);
        Press(view, Key.Home);

        var editor = view.FindControl<NodifyEditor>("Editor")!;
        var next   = KeyboardNavigationHandler.GetNext(editor.ContainerFromItem(root)!, NavigationDirection.Next);

        // Nodes are not tab stops: Tab goes on to the next control, not to another node.
        Assert.False(next is ItemContainer, $"Tab from a node went to another node ({next})");
        Assert.False(next is global::Avalonia.Visual v && v.GetVisualAncestors().Contains(editor),
                     $"Tab from a node stayed inside the canvas ({next})");
    }

    [AvaloniaFact]
    public void Minimap_HasAName()
    {
        var (_, view, _, _, _) = Setup();
        var minimap = view.FindControl<Control>("Minimap")!;
        Assert.False(string.IsNullOrEmpty(global::Avalonia.Automation.AutomationProperties.GetName(minimap)));
    }
}
