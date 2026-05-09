using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
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
}
