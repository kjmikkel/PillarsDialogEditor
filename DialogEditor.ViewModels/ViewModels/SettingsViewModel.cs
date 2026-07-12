using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly string           _gameDirectory;
    private readonly IFolderPicker    _picker;
    private readonly IFontScaleApplier _fontScaleApplier;

    [ObservableProperty] private string _backupDirectory;
    [ObservableProperty] private string _localizationFormat;
    [ObservableProperty] private double _selectedFontScale;

    /// Preset font-scale multipliers offered in Settings.
    public IReadOnlyList<double> FontScaleOptions { get; } = [1.0, 1.25, 1.5, 1.75, 2.0];

    /// Localization formats offered for the "Default localization format" picker.
    public IReadOnlyList<string> LocalizationFormatOptions { get; } = ["Csv", "Json", "Xliff"];

    // Live preview sizes — recomputed from SelectedFontScale as the user picks.
    public double PreviewBodyFontSize     => SelectedFontScale * 12;
    public double PreviewSubtitleFontSize => SelectedFontScale * 14;
    public double PreviewTitleFontSize    => SelectedFontScale * 18;

    public SettingsViewModel(string gameDirectory, IFolderPicker picker,
                             IFontScaleApplier? fontScaleApplier = null,
                             SpellDictionaryStore? spellStore = null)
    {
        _gameDirectory      = gameDirectory;
        _picker             = picker;
        _fontScaleApplier   = fontScaleApplier ?? new NullFontScaleApplier();
        _spellStore         = spellStore;
        _backupDirectory    = AppSettings.GetBackupPath(gameDirectory) ?? string.Empty;
        _localizationFormat = AppSettings.DefaultLocalizationFormat;
        _selectedFontScale  = AppSettings.FontScale;
    }

    // ── Spelling (three-layer dictionary; spec 2026-07-11) ──────────────────

    private readonly SpellDictionaryStore? _spellStore;

    /// The pinned source for Hunspell .aff/.dic pairs (verified 2026-07-11).
    public const string DictionarySourceUrl = "https://github.com/LibreOffice/dictionaries";

    /// View-wired seams so tests never shell out. Production wiring opens the
    /// folder in Explorer / the URL in the default browser.
    public Action<string>? FolderOpener { get; set; }
    public Action<string>? UrlOpener    { get; set; }

    public string DictionariesFolder => _spellStore?.DictionariesDirectory ?? string.Empty;

    public IReadOnlyList<string> DetectedDictionaryLanguages =>
        _spellStore?.AvailableLanguages ?? [];

    /// "Detected dictionaries: en, fr" / "No dictionaries detected yet."
    public string DetectedDictionariesText =>
        DetectedDictionaryLanguages.Count == 0
            ? Loc.Get("Settings_DetectedDictionaries_None")
            : Loc.Format("Settings_DetectedDictionaries",
                string.Join(", ", DetectedDictionaryLanguages));

    [RelayCommand]
    private void OpenDictionariesFolder()
    {
        if (_spellStore is null) return;
        var dir = _spellStore.DictionariesDirectory;
        try
        {
            Directory.CreateDirectory(dir);
            FolderOpener?.Invoke(dir);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to open dictionaries folder '{dir}': {ex.Message}");
        }
    }

    [RelayCommand]
    private void OpenDictionarySource()
    {
        try
        {
            UrlOpener?.Invoke(DictionarySourceUrl);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to open dictionary source link: {ex.Message}");
        }
    }

    partial void OnLocalizationFormatChanged(string value)
        => AppSettings.DefaultLocalizationFormat = value;

    partial void OnSelectedFontScaleChanged(double value)
    {
        AppSettings.FontScale = value;
        _fontScaleApplier.Apply(value);
        OnPropertyChanged(nameof(PreviewBodyFontSize));
        OnPropertyChanged(nameof(PreviewSubtitleFontSize));
        OnPropertyChanged(nameof(PreviewTitleFontSize));
    }

    // No-op applier so the ViewModel is testable without injecting one explicitly.
    private sealed class NullFontScaleApplier : IFontScaleApplier
    {
        public void Apply(double scale) { }
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
