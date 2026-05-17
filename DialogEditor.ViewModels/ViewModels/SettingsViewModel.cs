using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly string        _gameDirectory;
    private readonly IFolderPicker _picker;

    [ObservableProperty] private string _backupDirectory;

    public SettingsViewModel(string gameDirectory, IFolderPicker picker)
    {
        _gameDirectory   = gameDirectory;
        _picker          = picker;
        _backupDirectory = AppSettings.GetBackupPath(gameDirectory) ?? string.Empty;
    }

    [RelayCommand]
    private async Task BrowseBackupDirectory()
    {
        var path = await _picker.PickFolderAsync(Loc.Get("Settings_BackupDirectoryTitle"));
        if (path is null) return;
        AppSettings.SetBackupPath(_gameDirectory, path);
        BackupDirectory = path;
    }
}
