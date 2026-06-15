using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public AboutWindow(AboutViewModel viewModel) : this()
        => DataContext = viewModel;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
