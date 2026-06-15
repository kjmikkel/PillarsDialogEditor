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

    [AvaloniaFact]
    public void TabFocus_OnConnector_PansCameraToConnectorAnchor()
    {
        // Child is far enough away that its connectors sit outside the
        // initial 800px-wide viewport, which stays centred on the root.
        var vm = new ConversationViewModel(new StubDispatcher()) { IsEditable = true };
        var root  = CanvasNavigationServiceTests.MakeNode(0, 0, 0);
        var child = CanvasNavigationServiceTests.MakeNode(1, 2000, 0);
        root.OnSelected  = n => vm.SelectedNode = n;
        child.OnSelected = n => vm.SelectedNode = n;
        vm.Nodes.Add(root);
        vm.Nodes.Add(child);
        vm.Connections.Add(CanvasNavigationServiceTests.Connect(root, child));
        vm.SelectNode(root);

        var view = new ConversationView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        window.Show();

        var editor = (NodifyEditor)view.FindControl<Control>("Editor")!;

        // Simulate Tab moving Avalonia's keyboard focus to the child node's
        // "in" connector, as Nodify does once Tab order reaches that node.
        var childInput = ((global::Avalonia.Visual)editor).GetVisualDescendants()
            .OfType<NodeInput>()
            .First(i => ReferenceEquals(i.DataContext, child.Input));
        childInput.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
            Source = childInput,
        });

        var anchor = child.Input.Anchor;
        Assert.InRange(anchor.X, editor.ViewportLocation.X, editor.ViewportLocation.X + editor.ViewportSize.Width);
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
}
