using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
        // The theme picker is self-contained: it owns its ViewModel + applier rather than
        // sharing the window's SettingsViewModel, so the same control drops into PatchManager.
        ThemePicker.DataContext = new ThemePickerViewModel(new ThemeApplier());
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
