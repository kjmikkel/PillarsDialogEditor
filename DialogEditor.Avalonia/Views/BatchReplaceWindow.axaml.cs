using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class BatchReplaceWindow : Window
{
    public BatchReplaceWindow() => InitializeComponent();

    public BatchReplaceWindow(BatchReplaceViewModel vm) : this()
    {
        DataContext = vm;
        Opened += (_, _) => SearchBox.Focus();
    }
}
