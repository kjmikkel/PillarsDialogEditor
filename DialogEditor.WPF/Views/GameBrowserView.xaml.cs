using System.Windows;
using System.Windows.Controls;
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
}
