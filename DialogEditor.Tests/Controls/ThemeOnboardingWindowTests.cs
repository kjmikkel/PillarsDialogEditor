using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Controls;

/// <summary>
/// Gaps.md a11y item 15: first-run theme-onboarding window. Construction smoke test
/// mirroring the other headless dialog-construction tests (LanguageCodeDialog,
/// UnsavedChangesDialog, ...). See
/// docs/superpowers/specs/2026-06-14-theme-onboarding-design.md.
/// </summary>
public class ThemeOnboardingWindowTests : IDisposable
{
    public ThemeOnboardingWindowTests()
    {
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        Loc.Configure(new StubStringProvider());
    }

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    [AvaloniaFact]
    public void Constructs_WithoutThrowing()
    {
        var window = new ThemeOnboardingWindow();
        window.Show();
    }

    [AvaloniaFact]
    public void ThemePicker_ComboBox_HasAllFiveThemes()
    {
        var window = new ThemeOnboardingWindow();
        window.Show();

        var combo = window.FindControl<ThemePickerView>("ThemePicker")!
            .GetVisualDescendants()
            .OfType<ComboBox>()
            .Single();

        var vm = (ThemePickerViewModel)window.FindControl<ThemePickerView>("ThemePicker")!.DataContext!;
        Assert.Equal(5, vm.AvailableThemes.Count);
        Assert.Equal(5, combo.ItemCount);
    }

    [AvaloniaFact]
    public void ContinueButton_Exists_AndIsNamed()
    {
        var window = new ThemeOnboardingWindow();
        window.Show();

        var button = window.FindControl<Button>("ContinueButton");
        Assert.NotNull(button);
        Assert.False(string.IsNullOrWhiteSpace(button!.Content as string));
    }

    [AvaloniaFact]
    public void ContinueButton_Click_ClosesWindow()
    {
        var window = new ThemeOnboardingWindow();
        window.Show();

        var closed = false;
        window.Closed += (_, _) => closed = true;

        window.FindControl<Button>("ContinueButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.True(closed);
    }
}
