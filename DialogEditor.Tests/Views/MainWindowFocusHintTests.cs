using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Views;

/// <summary>
/// Gaps.md a11y item 5 Part B: MainWindow mirrors the focused control's
/// AutomationProperties.HelpText (set by AutomationHelpTextTests' sweep) into
/// MainWindowViewModel.FocusHintText, which DisplayStatusText then surfaces in the
/// status bar — giving sighted keyboard users the same explanation screen readers get.
/// </summary>
public class MainWindowFocusHintTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowFocusHintTests()
    {
        Loc.Configure(new StubStringProvider());
        // Fresh settings file so MainWindow's startup ReopenLastProjectOnStartup
        // (triggered by window.Show() -> OnOpened) finds no last project and is a
        // no-op — see project_flaky_test_appsettings.
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwfh_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    [AvaloniaFact]
    public void GotFocus_OnControlWithHelpText_SetsFocusHintText()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();

        var button = window.FindControl<Button>("SettingsButton")!;
        var expectedHint = AutomationProperties.GetHelpText(button);
        Assert.False(string.IsNullOrEmpty(expectedHint)); // sanity: Part A's sweep covered this button

        button.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });

        Assert.Equal(expectedHint, vm.FocusHintText);
        Assert.Equal(expectedHint, vm.DisplayStatusText);
    }

    [AvaloniaFact]
    public void GotFocus_OnElementWithoutHelpText_ClearsFocusHint()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        vm.FocusHintText = "stale hint";

        window.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Unspecified,
        });

        Assert.Equal(string.Empty, vm.FocusHintText);
        Assert.Equal(vm.StatusText, vm.DisplayStatusText);
    }
}
