using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Views;

/// <summary>
/// Gaps.md a11y item 8: StatusText (save/error/operation results) was never
/// announced to screen readers. A hidden "StatusLiveRegion" TextBlock, bound only
/// to StatusText and marked AutomationProperties.LiveSetting="Polite", announces
/// every StatusText change without re-announcing item 5's focus hints (which live
/// in DisplayStatusText, not StatusText, and are already announced by the normal
/// focus-description mechanism).
///
/// A headless probe (see design doc) confirmed TextBlockAutomationPeer.GetName()
/// mirrors Text and automatically raises a PropertyChanged notification when Text
/// changes — these tests assert on that peer-level behaviour directly.
/// </summary>
public class MainWindowStatusLiveRegionTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowStatusLiveRegionTests()
    {
        Loc.Configure(new StubStringProvider());
        // Fresh settings file so MainWindow's startup ReopenLastProjectOnStartup
        // (triggered by window.Show() -> OnOpened) finds no last project and is a
        // no-op — see project_flaky_test_appsettings.
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwslr_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    [AvaloniaFact]
    public void StatusLiveRegion_IsPoliteLiveRegion_ExposedToAutomation()
    {
        var window = new MainWindow();
        window.Show();

        var liveRegion = window.FindControl<TextBlock>("StatusLiveRegion");
        Assert.NotNull(liveRegion);

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(liveRegion!));

        var peer = ControlAutomationPeer.CreatePeerForElement(liveRegion!);
        Assert.True(peer.IsControlElement());
    }

    [AvaloniaFact]
    public void StatusLiveRegion_AnnouncesStatusTextChanges_NotFocusHintChanges()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();

        var liveRegion = window.FindControl<TextBlock>("StatusLiveRegion")!;
        var peer = ControlAutomationPeer.CreatePeerForElement(liveRegion);

        vm.StatusText = "Saved";
        Assert.Equal("Saved", peer.GetName());

        // A focus hint changes the visible DisplayStatusText, but must NOT change
        // the live region's announced name — that would duplicate the screen
        // reader's normal focus-description announcement.
        vm.FocusHintText = "Opens the settings dialog";
        Assert.Equal("Opens the settings dialog", vm.DisplayStatusText);
        Assert.Equal("Saved", peer.GetName());

        // A genuine status change while a focus hint is active still announces.
        vm.StatusText = "Project saved";
        Assert.Equal("Project saved", peer.GetName());
    }
}
