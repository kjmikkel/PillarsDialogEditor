using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class ConversationView : UserControl
{
    public ConversationView() => InitializeComponent();

    /// Raised when the user presses Enter on a selected node — MainWindow owns
    /// the detail panel and moves focus there (keyboard path into text editing).
    public event EventHandler? FocusDetailRequested;

    // Keyboard nudge steps (canvas units). Ctrl+arrow = fine, Ctrl+Shift+arrow = coarse.
    private const double NudgeStep      = 10;
    private const double NudgeStepLarge = 50;

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;

        var ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var none  = e.KeyModifiers == KeyModifiers.None;
        var step  = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? NudgeStepLarge : NudgeStep;

        var handled = e.Key switch
        {
            Key.Right when ctrl => vm.NudgeSelected(step, 0),
            Key.Left  when ctrl => vm.NudgeSelected(-step, 0),
            Key.Up    when ctrl => vm.NudgeSelected(0, -step),
            Key.Down  when ctrl => vm.NudgeSelected(0, step),

            Key.Right when none => vm.TryNavigate(CanvasNavDirection.Child),
            Key.Left  when none => vm.TryNavigate(CanvasNavDirection.Parent),
            Key.Up    when none => vm.TryNavigate(CanvasNavDirection.PreviousSibling),
            Key.Down  when none => vm.TryNavigate(CanvasNavDirection.NextSibling),

            Key.PageDown when none => vm.TryCycle(forward: true),
            Key.PageUp   when none => vm.TryCycle(forward: false),
            Key.Home     when none => vm.TrySelectRoot(),

            Key.Enter when none && vm.SelectedNode is not null => RaiseFocusDetail(),

            Key.Apps                                          => OpenSelectedNodeContextMenu(vm),
            Key.F10 when e.KeyModifiers == KeyModifiers.Shift => OpenSelectedNodeContextMenu(vm),

            Key.Escape when none => vm.Deselect(),

            _ => false,
        };

        if (!handled) return;
        FollowSelection(vm);
        e.Handled = true;
    }

    private bool RaiseFocusDetail()
    {
        FocusDetailRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool OpenSelectedNodeContextMenu(ConversationViewModel vm)
    {
        if (vm.SelectedNode is null) return false;

        // The ContextMenu lives on the nodify:Node inside the item template, not
        // on the ItemContainer itself — walk down from the realized container.
        var container = Editor.ContainerFromItem(vm.SelectedNode);
        var owner = container?.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.ContextMenu is not null);
        if (owner?.ContextMenu is not { } menu) return false;

        menu.Open(owner);
        return true;
    }

    // Keep the selected node on screen after every keyboard move.
    private void FollowSelection(ConversationViewModel vm)
    {
        if (vm.SelectedNode is { } node)
            Editor.BringIntoView(new global::Avalonia.Point(node.Location.X, node.Location.Y));
    }

    private void Editor_GotFocus(object? sender, GotFocusEventArgs e)
    {
        // Only keyboard-driven focus (Tab) auto-restores a selection. Pointer
        // focus must not: clicking empty canvas is how mouse users deselect.
        if (e.NavigationMethod != NavigationMethod.Tab) return;
        if (DataContext is not ConversationViewModel vm) return;
        if (vm.EnsureKeyboardSelection())
            FollowSelection(vm);
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void FocusEditor() => Editor.Focus();

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ConversationViewModel vm)
        {
            vm.SearchQuery = string.Empty;
            Editor.Focus();
            e.Handled = true;
        }
    }

    private void FitToScreen_Click(object? sender, RoutedEventArgs e) => Editor.FitToScreen();
    private void ZoomIn_Click(object? sender, RoutedEventArgs e)      => Editor.ZoomIn();
    private void ZoomOut_Click(object? sender, RoutedEventArgs e)     => Editor.ZoomOut();

    public void ScrollToNode(NodeViewModel node)
    {
        node.IsSelected = true;
        Editor.BringIntoView(new global::Avalonia.Point(node.Location.X, node.Location.Y));
    }

    private void CenterOnRoot_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;
        var root = vm.Nodes.FirstOrDefault(n => n.NodeId == 0);
        if (root is not null)
            Editor.BringIntoView(new global::Avalonia.Point(root.Location.X, root.Location.Y));
    }

    private void Editor_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;
        if (!vm.IsEditable) return;

        // Only create a node when tapping directly on the editor background
        if (e.Source is not Nodify.NodifyEditor) return;

        var screenPos  = e.GetPosition(Editor);
        var zoom       = Editor.ViewportZoom;
        var origin     = Editor.ViewportLocation;
        var (cx, cy)   = CanvasMath.ScreenToCanvas(screenPos.X, screenPos.Y, zoom, origin.X, origin.Y);
        var canvasPos  = new global::Avalonia.Point(cx, cy);

        var newId  = NodeIdAllocator.Next(vm.Nodes.Select(n => n.NodeId));
        var newNode = new NodeViewModel(
            new ConversationNode(newId, false, SpeakerCategory.Npc,
                string.Empty, string.Empty, [], [], [], "Conversation", "None"),
            new StringEntry(newId, string.Empty, string.Empty));

        vm.AddNode(newNode, new LayoutPoint((int)canvasPos.X, (int)canvasPos.Y));
        e.Handled = true;
    }
}
