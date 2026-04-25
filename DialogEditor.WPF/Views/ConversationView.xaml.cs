using System.Windows;
using System.Windows.Controls;
using DialogEditor.WPF.ViewModels;

namespace DialogEditor.WPF.Views;

public partial class ConversationView : UserControl
{
    public ConversationView() => InitializeComponent();

    private void CenterOnRoot_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;
        var root = vm.Nodes.FirstOrDefault(n => n.NodeId == 0);
        if (root is not null)
            Editor.BringIntoView(root.Location);
    }
}
