using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class GameBrowserView : UserControl
{
    public GameBrowserView() => InitializeComponent();

    private void TreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not GameBrowserViewModel vm) return;

        // In Avalonia 11, AddedItems may be empty; fall back to SelectedItem on the tree.
        foreach (var added in e.AddedItems)
        {
            if (added is ConversationItemViewModel item) { vm.SelectedItem = item; return; }
        }
        if (sender is TreeView tree && tree.SelectedItem is ConversationItemViewModel sel)
            vm.SelectedItem = sel;
    }
}
