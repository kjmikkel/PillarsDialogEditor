using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class SettingsViewModelAdditionalTests
{
    public SettingsViewModelAdditionalTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void Constructor_BackupDirectory_InitialisedFromAppSettings()
    {
        var picker = new StubFolderPicker();
        var vm = new SettingsViewModel("/game", picker);
        var expected = AppSettings.GetBackupPath("/game") ?? string.Empty;
        Assert.Equal(expected, vm.BackupDirectory);
    }

    [Fact]
    public async Task BrowseBackupDirectory_PickerCancelled_DirectoryUnchanged()
    {
        var picker = new StubFolderPicker(result: null);
        var vm = new SettingsViewModel(string.Empty, picker);
        var before = vm.BackupDirectory;
        await vm.BrowseBackupDirectoryCommand.ExecuteAsync(null);
        Assert.Equal(before, vm.BackupDirectory);
    }

    [Fact]
    public async Task BrowseBackupDirectory_PickerReturnsPick_DirectoryUpdated()
    {
        var picker = new StubFolderPicker(result: "/backups/here");
        var vm = new SettingsViewModel(string.Empty, picker);
        await vm.BrowseBackupDirectoryCommand.ExecuteAsync(null);
        Assert.Equal("/backups/here", vm.BackupDirectory);
    }
}
