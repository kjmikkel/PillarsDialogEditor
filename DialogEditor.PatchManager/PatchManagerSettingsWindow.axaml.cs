using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.ViewModels;

namespace DialogEditor.PatchManager;

public partial class PatchManagerSettingsWindow : Window
{
    public PatchManagerSettingsWindow()
    {
        InitializeComponent();
        ThemePicker.DataContext    = new ThemePickerViewModel(new ThemeApplier());
        LanguagePicker.DataContext = new LanguagePickerViewModel(new LanguageApplier(
            "avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.{0}.axaml",
            "avares://DialogEditor.PatchManager/Resources/Strings.{0}.axaml"));
        HintBar.AttachTo(this);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
