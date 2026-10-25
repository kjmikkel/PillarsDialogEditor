using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class BatchReplaceWindow : Window
{
    public BatchReplaceWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public BatchReplaceWindow(BatchReplaceViewModel vm) : this()
    {
        DataContext = vm;
        Opened += (_, _) => SearchBox.Focus();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
