using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class GameBrowserView : UserControl
{
    public GameBrowserView() => InitializeComponent();

    private void FilterBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is GameBrowserViewModel vm)
        {
            vm.FilterText = string.Empty;
            e.Handled = true;
        }
    }

    // Direct tap on each conversation item — bypasses TreeView selection quirks.
    private void ConversationItem_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is TextBlock tb &&
            tb.DataContext is ConversationItemViewModel item &&
            DataContext is GameBrowserViewModel vm)
        {
            vm.SelectedItem = item;
            e.Handled = true;
        }
    }

    // Belt-and-suspenders: also handle via SelectionChanged / SelectedItem binding.
    private void TreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not GameBrowserViewModel vm) return;

        foreach (var added in e.AddedItems)
        {
            if (added is ConversationItemViewModel item) { vm.SelectedItem = item; return; }
        }
        if (sender is TreeView tree && tree.SelectedItem is ConversationItemViewModel sel)
            vm.SelectedItem = sel;
    }
}
