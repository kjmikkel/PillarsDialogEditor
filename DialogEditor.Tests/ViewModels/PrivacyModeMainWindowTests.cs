using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue #270 on the main window and the exception report: masked display, real values kept.
public class PrivacyModeMainWindowTests : IDisposable
{
    private sealed class FormatProvider : IStringProvider
    {
        public string Get(string key) => key switch
        {
            "Status_FolderLoaded"         => "{0} — {1}",
            "Status_RecentProjectMissing" => "missing {0}",
            _                             => key,
        };
        public bool TryGet(string key, out string value) { value = Get(key); return true; }
    }

    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"priv_mw_{Guid.NewGuid():N}.json");

    public PrivacyModeMainWindowTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
        PrivacyMode.ResetForTests();
    }

    public void Dispose()
    {
        PrivacyMode.ResetForTests();
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_settingsPath); } catch { /* best-effort */ }
    }

    private static MainWindowViewModel Vm()
        => new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    // ── Toggle ────────────────────────────────────────────────────────────

    [Fact]
    public void IsPrivacyModeEnabled_ReflectsAndPersistsTheSetting()
    {
        var vm = Vm();
        Assert.False(vm.IsPrivacyModeEnabled);

        vm.IsPrivacyModeEnabled = true;

        Assert.True(AppSettings.PrivacyMode);
        Assert.True(PrivacyMode.IsOn);
    }

    [Fact]
    public void IsPrivacyModeEnabled_TrueWhenForcedForSession_WithoutPersisting()
    {
        PrivacyMode.ForceOnForSession();
        var vm = Vm();
        Assert.True(vm.IsPrivacyModeEnabled);
        Assert.False(AppSettings.PrivacyMode);
    }

    // ── Recent projects ───────────────────────────────────────────────────

    [Fact]
    public void RecentProjects_ToolTipMasked_WhenOn_CommandParameterStaysReal()
    {
        const string real = @"D:\Work\Secret Folder\story.dialogproject";
        AppSettings.AddRecentProject(real);
        var vm = Vm();
        Assert.Equal(real, vm.RecentProjectsMenuItems[0].ToolTip);

        PrivacyMode.SetPersisted(true);

        var item = vm.RecentProjectsMenuItems[0];
        Assert.Equal(@"…\story.dialogproject", item.ToolTip);
        Assert.Equal(real, item.CommandParameter);
        Assert.Equal(real, AppSettings.RecentProjects[0]);
    }

    // ── Status bar ────────────────────────────────────────────────────────

    [Fact]
    public void LoadDirectory_StatusBar_MasksFolder_WhenOn()
    {
        using var game = new FakePoe2Game("en");
        AppSettings.MarkGameDirectoryKnown(game.Root);
        Loc.Configure(new FormatProvider());
        PrivacyMode.ForceOnForSession();

        var vm = Vm();
        typeof(MainWindowViewModel)
            .GetMethod("LoadDirectory", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [game.Root]);

        Assert.DoesNotContain(game.Root, vm.StatusText);
        Assert.Contains(PathDisplay.ForDisplay(game.Root)!, vm.StatusText);
    }

    [Fact]
    public void RecentProjectMissing_StatusBar_MasksPath_WhenOn()
    {
        const string ghost = @"Z:\Secret Folder\ghost.dialogproject";
        AppSettings.AddRecentProject(ghost);
        Loc.Configure(new FormatProvider());
        PrivacyMode.ForceOnForSession();
        var vm = Vm();

        vm.OpenRecentProjectCommand.Execute(ghost);

        Assert.DoesNotContain("Secret Folder", vm.StatusText);
        Assert.Equal(@"missing …\ghost.dialogproject", vm.StatusText);
        Assert.Contains(Path.GetFullPath(ghost), AppSettings.RecentProjects);
    }

    // ── Exception report ──────────────────────────────────────────────────

    [Fact]
    public void ExceptionReport_LogPathDisplay_MaskedWhenOn_LogPathUntouched()
    {
        var vm = new ExceptionReportViewModel(new InvalidOperationException("x"),
            @"D:\logs\app.log", "https://example.com/issues");
        Assert.Equal(@"D:\logs\app.log", vm.LogPathDisplay);

        PrivacyMode.ForceOnForSession();

        Assert.Equal(@"…\app.log", vm.LogPathDisplay);
        Assert.Equal(@"D:\logs\app.log", vm.LogPath);
    }
}
