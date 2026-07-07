using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelWhatsNewTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowViewModelWhatsNewTests()
    {
        Loc.Configure(new StubStringProvider());
        // Isolate AppSettings so the VM constructor doesn't auto-load the real game folder.
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_wn_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    private const string Changelog = """
        # Changelog

        ## [1.1.0] - 2026-02-01
        ### Added
        - New thing

        ## [1.0.0] - 2026-01-01
        ### Added
        - First thing
        """;

    private static (MainWindowViewModel vm, List<ChangelogViewModel> shown, List<string> persisted)
        Wire(string lastSeen, string current, string? changelog)
    {
        var vm        = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        var shown     = new List<ChangelogViewModel>();
        var persisted = new List<string>();
        vm.ChangelogReader        = () => changelog;
        vm.ShowChangelog          = cvm => shown.Add(cvm);
        vm.LastSeenVersionGetter  = () => lastSeen;
        vm.LastSeenVersionSetter  = v => persisted.Add(v);
        vm.CurrentVersionProvider = () => current;
        return (vm, shown, persisted);
    }

    [Fact]
    public void Upgrade_ShowsWhatsNew_AndPersistsCurrent()
    {
        var (vm, shown, persisted) = Wire(lastSeen: "1.0.0", current: "1.1.0", Changelog);
        vm.ShowWhatsNewIfUpdated();
        var cvm = Assert.Single(shown);
        Assert.True(cvm.IsWhatsNew);
        Assert.Equal(["1.1.0"], cvm.Releases.Select(r => r.Version));
        Assert.Equal(["1.1.0"], persisted);
    }

    [Fact]
    public void SameVersion_ShowsNothing_ButPersists()
    {
        var (vm, shown, persisted) = Wire("1.1.0", "1.1.0", Changelog);
        vm.ShowWhatsNewIfUpdated();
        Assert.Empty(shown);
        Assert.Equal(["1.1.0"], persisted);
    }

    [Fact]
    public void EmptyLastSeen_ShowsNothing_ButPersistsBaseline()
    {
        var (vm, shown, persisted) = Wire("", "1.1.0", Changelog);
        vm.ShowWhatsNewIfUpdated();
        Assert.Empty(shown);
        Assert.Equal(["1.1.0"], persisted);
    }

    [Fact]
    public void UnknownVersion_ShowsNothing_AndDoesNotPersist()
    {
        var (vm, shown, persisted) = Wire("1.0.0", "unknown", Changelog);
        vm.ShowWhatsNewIfUpdated();
        Assert.Empty(shown);
        Assert.Empty(persisted);
    }

    [Fact]
    public void EmptyChangelogOnUpgrade_ShowsNothing_ButPersists()
    {
        var (vm, shown, persisted) = Wire("1.0.0", "1.1.0", changelog: null);
        vm.ShowWhatsNewIfUpdated();
        Assert.Empty(shown);
        Assert.Equal(["1.1.0"], persisted);
    }
}
