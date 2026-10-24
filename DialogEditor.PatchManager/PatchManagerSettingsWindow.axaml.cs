using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.PatchManager;

public partial class PatchManagerSettingsWindow : Window
{
    private bool _populating;

    public PatchManagerSettingsWindow()
    {
        InitializeComponent();
        ThemePicker.DataContext    = new ThemePickerViewModel(new ThemeApplier());
        LanguagePicker.DataContext = new LanguagePickerViewModel(new LanguageApplier(
            "avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.{0}.axaml",
            "avares://DialogEditor.PatchManager/Resources/Strings.{0}.axaml"));
        // Show the effective state (checked when --privacy-mode forced it on). The
        // handler is wired in XAML, so _populating stops this from persisting it.
        _populating = true;
        PrivacyCheck.IsChecked = PrivacyMode.IsOn;
        _populating = false;
        HintBar.AttachTo(this);
    }

    private void PrivacyCheck_Changed(object? sender, RoutedEventArgs e)
    {
        if (_populating) return;
        PrivacyMode.SetPersisted(PrivacyCheck.IsChecked == true);
        // A session override keeps privacy on whatever was saved; keep the box truthful.
        PrivacyCheck.IsChecked = PrivacyMode.IsOn;
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
