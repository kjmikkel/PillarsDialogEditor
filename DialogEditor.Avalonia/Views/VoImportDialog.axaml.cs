using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class VoImportDialog : Window
{
    private enum PlayingSlot { None, CurrentPrimary, CurrentFem, SourcePrimary, SourceFem }

    private readonly IVoImporter    _importer = null!;
    private readonly IVoAudioPlayer _player   = null!;
    private readonly VoImportPaths  _paths    = null!;

    private string?     _primarySource;
    private string?     _femSource;
    private PlayingSlot _playingSlot = PlayingSlot.None;
    private WemQuality  _quality     = WemQuality.Medium;

    /// Set to non-null when the user clicks Import.
    public VoImportDialogResult? Result { get; private set; }

    // Parameterless ctor so the XAML compiler can embed the type (avoids AVLN3001).
    public VoImportDialog() => InitializeComponent();

    public VoImportDialog(IVoImporter importer, VoImportPaths paths, IVoAudioPlayer player)
    {
        InitializeComponent();
        _importer = importer;
        _paths    = paths;
        _player   = player;

        _player.PlaybackStopped += OnPlaybackStopped;
        Closed += (_, _) =>
        {
            _player.PlaybackStopped -= OnPlaybackStopped;
            _player.Stop();
        };

        // ── Current → New grid state (2026-07-02 layout spec) ──
        // The Current column exists only when a current file is on disk at open
        // time; visibility and window width are fixed per-open because files
        // cannot appear while the dialog is up.
        var primaryExists = File.Exists(paths.PrimaryDestinationPath);
        var femExists     = paths.FemDestinationPath is not null
                         && File.Exists(paths.FemDestinationPath);
        var hasCurrent    = primaryExists || femExists;

        if (hasCurrent)
        {
            // Three columns need more room than the collapsed 500px default.
            Width = 640;
            CurrentPrimaryLabel.Text = primaryExists
                ? Path.GetFileName(paths.PrimaryDestinationPath)
                : Loc.Get("VoImport_NoCurrentFile");
            PlayCurrentPrimaryButton.IsVisible = primaryExists;
        }
        else
        {
            VariantGrid.ColumnDefinitions[1].Width = new GridLength(0);
            CurrentHeader.IsVisible      = false;
            NewHeader.IsVisible          = false;
            CurrentPrimaryCell.IsVisible = false;
        }

        // Female row visible only when the node has female text.
        if (paths.FemDestinationPath is not null)
        {
            FemRowLabel.IsVisible   = true;
            FemSourceCell.IsVisible = true;
            if (hasCurrent)
            {
                CurrentFemCell.IsVisible = true;
                CurrentFemLabel.Text = femExists
                    ? Path.GetFileName(paths.FemDestinationPath)
                    : Loc.Get("VoImport_NoCurrentFile");
                PlayCurrentFemButton.IsVisible = femExists;
            }
        }
    }

    // ── Current VO play buttons ───────────────────────────────────────────

    private void PlayCurrentPrimary_Click(object? sender, RoutedEventArgs e)
        => TogglePlay(PlayingSlot.CurrentPrimary, _paths.PrimaryDestinationPath);

    private void PlayCurrentFem_Click(object? sender, RoutedEventArgs e)
        => TogglePlay(PlayingSlot.CurrentFem, _paths.FemDestinationPath!);

    // ── Source preview play buttons ───────────────────────────────────────

    private void PlaySourcePrimary_Click(object? sender, RoutedEventArgs e)
        => TogglePlay(PlayingSlot.SourcePrimary, _primarySource!);

    private void PlaySourceFem_Click(object? sender, RoutedEventArgs e)
        => TogglePlay(PlayingSlot.SourceFem, _femSource!);

    private void TogglePlay(PlayingSlot slot, string path)
    {
        if (_playingSlot == slot)
        {
            _player.Stop();
            _playingSlot = PlayingSlot.None;
        }
        else
        {
            // player.Play() calls StopAndCleanup() internally, so any in-progress
            // playback is stopped before the new track starts.
            _player.Play(path);
            _playingSlot = slot;
        }
        UpdatePlayGlyphs();
    }

    private void OnPlaybackStopped()
    {
        _playingSlot = PlayingSlot.None;
        UpdatePlayGlyphs();
    }

    private void UpdatePlayGlyphs()
    {
        SetPlayGlyph(PlayCurrentPrimaryButton, PlayingSlot.CurrentPrimary,
            "ToolTip_VoCurrentPlay_Primary");
        SetPlayGlyph(PlayCurrentFemButton,     PlayingSlot.CurrentFem,
            "ToolTip_VoCurrentPlay_Fem");
        SetPlayGlyph(PlaySourcePrimaryButton,  PlayingSlot.SourcePrimary,
            "ToolTip_VoPreviewPlay_Primary");
        SetPlayGlyph(PlaySourceFemButton,      PlayingSlot.SourceFem,
            "ToolTip_VoPreviewPlay_Fem");
    }

    private void SetPlayGlyph(Button btn, PlayingSlot slot, string playTooltipKey)
    {
        var playing = _playingSlot == slot;
        btn.Content = playing ? "■" : "▶";
        ToolTip.SetTip(btn, Loc.Get(playing ? "ToolTip_VoPreviewStop" : playTooltipKey));
    }

    // ── Quality ──────────────────────────────────────────────────────────

    private void QualityLow_Checked(object?    sender, RoutedEventArgs e) => _quality = WemQuality.Low;
    private void QualityMedium_Checked(object? sender, RoutedEventArgs e) => _quality = WemQuality.Medium;
    private void QualityHigh_Checked(object?   sender, RoutedEventArgs e) => _quality = WemQuality.High;

    // ── Browse / Clear ───────────────────────────────────────────────────

    private async void BrowsePrimary_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickVoFileAsync();
        if (path is null) return;
        _primarySource                    = path;
        PrimarySourceLabel.Text           = Path.GetFileName(path);
        PlaySourcePrimaryButton.IsVisible = true;
        ClearPrimaryButton.IsVisible      = true;
        UpdateQualityAndWarning();
        UpdateOkButton();
    }

    private void ClearPrimary_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingSlot == PlayingSlot.SourcePrimary) { _player.Stop(); _playingSlot = PlayingSlot.None; }
        _primarySource                    = null;
        PrimarySourceLabel.Text           = Loc.Get("VoImport_NoFileChosen");
        PlaySourcePrimaryButton.IsVisible = false;
        ClearPrimaryButton.IsVisible      = false;
        UpdatePlayGlyphs();
        UpdateQualityAndWarning();
        UpdateOkButton();
    }

    private async void BrowseFem_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickVoFileAsync();
        if (path is null) return;
        _femSource                    = path;
        FemSourceLabel.Text           = Path.GetFileName(path);
        PlaySourceFemButton.IsVisible = true;
        ClearFemButton.IsVisible      = true;
        UpdateQualityAndWarning();
    }

    private void ClearFem_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingSlot == PlayingSlot.SourceFem) { _player.Stop(); _playingSlot = PlayingSlot.None; }
        _femSource                    = null;
        FemSourceLabel.Text           = Loc.Get("VoImport_NoFileChosen");
        PlaySourceFemButton.IsVisible = false;
        ClearFemButton.IsVisible      = false;
        UpdatePlayGlyphs();
        UpdateQualityAndWarning();
    }

    // ── OK / Cancel ──────────────────────────────────────────────────────

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (_primarySource is null) return;
        Result = new VoImportDialogResult(_primarySource, _femSource, _quality);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void DownloadWwise_Click(object? sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://www.audiokinetic.com/en/products/wwise/") { UseShellExecute = true });

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string?> PickVoFileAsync()
    {
        var options = new FilePickerOpenOptions
        {
            Title          = Loc.Get("VoImport_PickerTitle"),
            AllowMultiple  = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("VoImport_FileType_All"))
                    { Patterns = ["*.wem", "*.wav"] },
                new FilePickerFileType(Loc.Get("VoImport_FileType_Wem"))
                    { Patterns = ["*.wem"] },
                new FilePickerFileType(Loc.Get("VoImport_FileType_Wav"))
                    { Patterns = ["*.wav"] },
            ],
        };
        var files = await StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private void UpdateOkButton()
    {
        var isWav = _primarySource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true;
        OkButton.IsEnabled = _primarySource is not null && (!isWav || _importer.IsWwiseAvailable);
    }

    private void UpdateQualityAndWarning()
    {
        var anyWav =
            (_primarySource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true) ||
            (_femSource?.EndsWith(".wav",     StringComparison.OrdinalIgnoreCase) == true);
        QualityPanel.IsVisible      = anyWav;
        WwiseWarningPanel.IsVisible = anyWav && !_importer.IsWwiseAvailable;
    }

}
