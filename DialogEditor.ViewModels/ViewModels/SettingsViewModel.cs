using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly string        _gameDirectory;
    private readonly IFolderPicker _picker;

    // The scale that FontScaleApplier actually applied at this session's startup —
    // captured once so ShowRestartNotice can detect when the user picks a different
    // value (Gaps item 6 part B).
    private readonly double _appliedFontScale;

    [ObservableProperty] private string _backupDirectory;
    [ObservableProperty] private string _localizationFormat;
    [ObservableProperty] private double _selectedFontScale;

    /// Preset font-scale multipliers offered in Settings.
    public IReadOnlyList<double> FontScaleOptions { get; } = [1.0, 1.25, 1.5, 1.75, 2.0];

    /// Localization formats offered for the "Default localization format" picker.
    public IReadOnlyList<string> LocalizationFormatOptions { get; } = ["Csv", "Json", "Xliff"];

    // Live preview sizes, independent of the FontSize.* resource tokens (which stay
    // static until restart) — recomputed from SelectedFontScale as the user picks.
    public double PreviewBodyFontSize     => SelectedFontScale * 12;
    public double PreviewSubtitleFontSize => SelectedFontScale * 14;
    public double PreviewTitleFontSize    => SelectedFontScale * 18;

    /// True once the selected scale differs from the one applied at launch.
    public bool ShowRestartNotice => SelectedFontScale != _appliedFontScale;

    public SettingsViewModel(string gameDirectory, IFolderPicker picker)
    {
        _gameDirectory      = gameDirectory;
        _picker             = picker;
        _backupDirectory    = AppSettings.GetBackupPath(gameDirectory) ?? string.Empty;
        _localizationFormat = AppSettings.DefaultLocalizationFormat;
        _appliedFontScale   = AppSettings.FontScale;
        _selectedFontScale  = _appliedFontScale;
    }

    partial void OnLocalizationFormatChanged(string value)
        => AppSettings.DefaultLocalizationFormat = value;

    partial void OnSelectedFontScaleChanged(double value)
    {
        AppSettings.FontScale = value;
        OnPropertyChanged(nameof(PreviewBodyFontSize));
        OnPropertyChanged(nameof(PreviewSubtitleFontSize));
        OnPropertyChanged(nameof(PreviewTitleFontSize));
        OnPropertyChanged(nameof(ShowRestartNotice));
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
