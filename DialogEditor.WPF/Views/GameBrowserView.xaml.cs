using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DialogEditor.WPF.ViewModels;

namespace DialogEditor.WPF.Views;

public partial class GameBrowserView : UserControl
{
    public GameBrowserView() => InitializeComponent();

    private void TreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (DataContext is GameBrowserViewModel vm && e.NewValue is ConversationItemViewModel item)
            vm.SelectedItem = item;
    }

    private void FilterBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is GameBrowserViewModel vm)
                vm.FilterText = string.Empty;
            e.Handled = true;
        }
    }
}
