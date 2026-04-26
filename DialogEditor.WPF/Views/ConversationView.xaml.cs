using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DialogEditor.WPF.ViewModels;

namespace DialogEditor.WPF.Views;

public partial class ConversationView : UserControl
{
    public ConversationView() => InitializeComponent();

    // Called by MainWindow when Ctrl+F is pressed anywhere in the window
    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    private void SearchBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is ConversationViewModel vm)
                vm.SearchQuery = string.Empty;
            Editor.Focus();
            e.Handled = true;
        }
    }

    private void CenterOnRoot_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;
        var root = vm.Nodes.FirstOrDefault(n => n.NodeId == 0);
        if (root is not null)
            Editor.BringIntoView(root.Location);
    }
}
