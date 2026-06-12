using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.Tests.ViewModels;
using DialogEditor.ViewModels;

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
}
