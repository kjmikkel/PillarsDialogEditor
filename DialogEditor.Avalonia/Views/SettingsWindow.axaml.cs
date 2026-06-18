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
        // Each picker is self-contained: it owns its ViewModel + applier rather than
        // sharing the window's SettingsViewModel, so the same controls drop into PatchManager.
        ThemePicker.DataContext    = new ThemePickerViewModel(new ThemeApplier());
        LanguagePicker.DataContext = new LanguagePickerViewModel(new LanguageApplier(
            "avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.{0}.axaml",
            "avares://DialogEditor.Avalonia/Resources/Strings.{0}.axaml"));
        HintBar.AttachTo(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
