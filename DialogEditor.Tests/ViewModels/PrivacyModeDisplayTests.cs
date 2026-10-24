using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Issue 270: masked paths are display-only. Every test that reads a *Display property
/// also asserts the stored value is untouched.
public class PrivacyModeDisplayTests : IDisposable
{
    private const string Game = @"D:\GOG Games\Pillars of Eternity II Deadfire";

    public PrivacyModeDisplayTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = Path.GetTempFileName();
        PrivacyMode.ResetForTests();
    }

    public void Dispose()
    {
        PrivacyMode.ResetForTests();
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) try { File.Delete(path); } catch { /* best-effort */ }
    }

    // ── Settings window ───────────────────────────────────────────────────

    private static SettingsViewModel Settings()
    {
        AppSettings.SetBackupPath(@"D:\game", @"E:\Backups\PoE2");
        return new SettingsViewModel(@"D:\game", new StubFolderPicker());
    }

    [Fact]
    public void Settings_BackupDirectoryDisplay_PlainWhenOff()
        => Assert.Equal(@"E:\Backups\PoE2", Settings().BackupDirectoryDisplay);

    [Fact]
    public void Settings_BackupDirectoryDisplay_MaskedWhenOn_StoredValueUntouched()
    {
        var vm = Settings();
        PrivacyMode.SetPersisted(true);

        Assert.Equal(@"…\PoE2", vm.BackupDirectoryDisplay);
        Assert.Equal(@"E:\Backups\PoE2", vm.BackupDirectory);
        Assert.Equal(@"E:\Backups\PoE2", AppSettings.GetBackupPath(@"D:\game"));
    }

    [Fact]
    public void Settings_BackupDirectoryDisplay_RaisesWhenModeChanges()
    {
        var vm = Settings();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        PrivacyMode.SetPersisted(true);
        Assert.Contains("BackupDirectoryDisplay", raised);
    }

    // ── Patch Manager ─────────────────────────────────────────────────────

    private static PatchManagerViewModel PatchManager(StubFilePicker? picker = null)
        => new(new StubFolderPicker(), picker ?? new StubFilePicker()) { GameFolder = Game };

    [Fact]
    public void PatchManager_GameFolderDisplay_PlainAndEditableWhenOff()
    {
        var vm = PatchManager();
        Assert.Equal(Game, vm.GameFolderDisplay);
        Assert.False(vm.IsPrivacyMode);
    }

    [Fact]
    public async Task PatchManager_GameFolderDisplay_MaskedWhenOn_GameFolderAndSavedPatchListUntouched()
    {
        var file = Path.Combine(Path.GetTempPath(), $"priv-{Guid.NewGuid():N}.patchlist");
        try
        {
            var vm = PatchManager(new StubFilePicker(saveResult: file));
            PrivacyMode.SetPersisted(true);

            Assert.True(vm.IsPrivacyMode);
            Assert.Equal(@"…\Pillars of Eternity II Deadfire", vm.GameFolderDisplay);
            Assert.Equal(Game, vm.GameFolder);

            await vm.SaveLoadOrderCommand.ExecuteAsync(null);
            Assert.Equal(Game, PatchListSerializer.LoadFromFile(file).GameFolder);
        }
        finally { try { File.Delete(file); } catch { /* best-effort */ } }
    }

    [Fact]
    public void PatchManager_GameFolderDisplay_FollowsGameFolderChanges()
    {
        var vm = PatchManager();
        PrivacyMode.SetPersisted(true);
        vm.GameFolder = @"E:\Other\Game";
        Assert.Equal(@"…\Game", vm.GameFolderDisplay);
    }
}
