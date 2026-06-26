using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class VoImportDialog : Window
{
    private enum PlayingSlot { None, Primary, Fem }

    private readonly IVoImporter    _importer = null!;
    private readonly IVoAudioPlayer _player   = null!;

    private string?     _primarySource;
    private string?     _femSource;
    private PlayingSlot _playingSlot = PlayingSlot.None;
    private WemQuality  _quality     = WemQuality.Medium;

    /// Set to non-null when the user clicks OK.
    public VoImportDialogResult? Result { get; private set; }

    // Parameterless ctor so the XAML compiler can embed the type (avoids AVLN3001).
    public VoImportDialog() => InitializeComponent();

    public VoImportDialog(IVoImporter importer, VoImportPaths paths, IVoAudioPlayer player)
    {
        InitializeComponent();
        _importer = importer;
        _player   = player;

        _player.PlaybackStopped += OnPlaybackStopped;
        Closed += (_, _) =>
        {
            _player.PlaybackStopped -= OnPlaybackStopped;
            _player.Stop();
        };

        PrimaryLabel.Text = Path.GetFileName(paths.PrimaryDestinationPath);
        if (paths.FemDestinationPath is not null)
            FemLabel.Text = Path.GetFileName(paths.FemDestinationPath);
    }

    // ── Play buttons ─────────────────────────────────────────────────────

    private void PlayPrimary_Click(object? sender, RoutedEventArgs e)
    {
        if (_primarySource is null) return;
        if (_playingSlot == PlayingSlot.Primary)
        {
            _player.Stop();
            _playingSlot = PlayingSlot.None;
        }
        else
        {
            _player.Play(_primarySource);
            _playingSlot = PlayingSlot.Primary;
        }
        UpdatePlayGlyphs();
    }

    private void PlayFem_Click(object? sender, RoutedEventArgs e)
    {
        if (_femSource is null) return;
        if (_playingSlot == PlayingSlot.Fem)
        {
            _player.Stop();
            _playingSlot = PlayingSlot.None;
        }
        else
        {
            _player.Play(_femSource);
            _playingSlot = PlayingSlot.Fem;
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
        PlayPrimaryButton.Content = _playingSlot == PlayingSlot.Primary ? "■" : "▶";
        PlayFemButton.Content     = _playingSlot == PlayingSlot.Fem     ? "■" : "▶";

        ToolTip.SetTip(PlayPrimaryButton, _playingSlot == PlayingSlot.Primary
            ? Loc.Get("ToolTip_VoPreviewStop")
            : Loc.Get("ToolTip_VoPreviewPlay_Primary"));
        ToolTip.SetTip(PlayFemButton, _playingSlot == PlayingSlot.Fem
            ? Loc.Get("ToolTip_VoPreviewStop")
            : Loc.Get("ToolTip_VoPreviewPlay_Fem"));
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
        _primarySource               = path;
        PrimaryLabel.Text            = Path.GetFileName(path);
        ClearPrimaryButton.IsVisible = true;
        PlayPrimaryButton.IsVisible  = true;
        UpdateWwiseWarning();
        UpdateOkButton();
    }

    private void ClearPrimary_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingSlot == PlayingSlot.Primary) { _player.Stop(); _playingSlot = PlayingSlot.None; }
        _primarySource               = null;
        PrimaryLabel.Text            = "—";
        ClearPrimaryButton.IsVisible = false;
        PlayPrimaryButton.IsVisible  = false;
        UpdatePlayGlyphs();
        UpdateWwiseWarning();
        UpdateOkButton();
    }

    private async void BrowseFem_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickVoFileAsync();
        if (path is null) return;
        _femSource               = path;
        FemLabel.Text            = Path.GetFileName(path);
        ClearFemButton.IsVisible = true;
        PlayFemButton.IsVisible  = true;
        UpdateWwiseWarning();
    }

    private void ClearFem_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingSlot == PlayingSlot.Fem) { _player.Stop(); _playingSlot = PlayingSlot.None; }
        _femSource               = null;
        FemLabel.Text            = "—";
        ClearFemButton.IsVisible = false;
        PlayFemButton.IsVisible  = false;
        UpdatePlayGlyphs();
        UpdateWwiseWarning();
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

    private void UpdateWwiseWarning()
    {
        var anyWav =
            (_primarySource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true) ||
            (_femSource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true);
        WwiseWarningPanel.IsVisible = anyWav && !_importer.IsWwiseAvailable;
        QualityPanel.IsEnabled      = anyWav;
    }
}
