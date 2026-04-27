using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class GameBrowserView : UserControl
{
    public GameBrowserView() => InitializeComponent();

    private void TreeView_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (DataContext is GameBrowserViewModel vm &&
            e.AddedItems.Count > 0 &&
            e.AddedItems[0] is ConversationItemViewModel item)
            vm.SelectedItem = item;
    }
}
