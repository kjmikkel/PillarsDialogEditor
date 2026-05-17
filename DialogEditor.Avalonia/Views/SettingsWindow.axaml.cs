using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DialogEditor.Avalonia.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow() => InitializeComponent();

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
