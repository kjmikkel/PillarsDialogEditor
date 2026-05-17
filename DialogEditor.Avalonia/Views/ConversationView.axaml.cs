using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class ConversationView : UserControl
{
    public ConversationView() => InitializeComponent();

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

        // Only create a node when tapping directly on the editor background
        if (e.Source is not Nodify.NodifyEditor) return;

        var screenPos  = e.GetPosition(Editor);
        // Convert screen → canvas: canvas = screen / zoom + viewportOrigin
        var zoom       = Editor.ViewportZoom;
        var origin     = Editor.ViewportLocation;
        var canvasPos  = new global::Avalonia.Point(
            screenPos.X / zoom + origin.X,
            screenPos.Y / zoom + origin.Y);

        var newId  = NodeIdAllocator.Next(vm.Nodes.Select(n => n.NodeId));
        var newNode = new NodeViewModel(
            new ConversationNode(newId, false, SpeakerCategory.Npc,
                string.Empty, string.Empty, [], [], [], "Conversation", "None"),
            new StringEntry(newId, string.Empty, string.Empty));

        vm.AddNode(newNode, new LayoutPoint((int)canvasPos.X, (int)canvasPos.Y));
        e.Handled = true;
    }
}
