using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class ChangelogWindow : Window
{
    public ChangelogWindow() => InitializeComponent();

    public ChangelogWindow(ChangelogViewModel viewModel) : this()
        => DataContext = viewModel;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
