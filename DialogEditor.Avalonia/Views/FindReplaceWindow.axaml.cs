using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class FindReplaceWindow : Window
{
    public FindReplaceWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public FindReplaceWindow(FindReplaceViewModel vm) : this()
    {
        DataContext = vm;
        Opened += (_, _) => SearchBox.Focus();
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
