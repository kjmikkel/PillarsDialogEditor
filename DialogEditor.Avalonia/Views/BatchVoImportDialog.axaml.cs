using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class BatchVoImportDialog : Window
{
    private readonly BatchVoImportViewModel _vm     = null!;
    private readonly IVoAudioPlayer         _player = null!;

    private BatchVoRowViewModel? _playingRow;
    private bool                 _playingPrimary;

    // Parameterless ctor so the AXAML compiler can embed the type (avoids AVLN3001).
    public BatchVoImportDialog() => InitializeComponent();

    public BatchVoImportDialog(
        BatchVoImportViewModel vm,
        IVoAudioPlayer         player)
    {
        InitializeComponent();
        _vm     = vm;
        _player = player;

        DataContext = vm;

        _player.PlaybackStopped += OnPlaybackStopped;
        Closed += (_, _) =>
        {
            _player.PlaybackStopped -= OnPlaybackStopped;
            _player.Stop();
        };
    }

    // ── Quality ──────────────────────────────────────────────────────────

    private void QualityLow_Checked(object?    sender, RoutedEventArgs e) => _vm.Quality = WemQuality.Low;
    private void QualityMedium_Checked(object? sender, RoutedEventArgs e) => _vm.Quality = WemQuality.Medium;
    private void QualityHigh_Checked(object?   sender, RoutedEventArgs e) => _vm.Quality = WemQuality.High;

    // ── Browse / Clear ───────────────────────────────────────────────────

    private async void BrowsePrimary_Click(object? sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row is null) return;
        var path = await PickVoFileAsync();
        if (path is null) return;
        row.PrimarySourcePath = path;
        _vm.OnRowChanged();
    }

    private void ClearPrimary_Click(object? sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row is null) return;
        if (_playingRow == row && _playingPrimary) StopPlayback();
        row.PrimarySourcePath = null;
        _vm.OnRowChanged();
    }

    private async void BrowseFem_Click(object? sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row is null) return;
        var path = await PickVoFileAsync();
        if (path is null) return;
        row.FemSourcePath = path;
    }

    private void ClearFem_Click(object? sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row is null) return;
        if (_playingRow == row && !_playingPrimary) StopPlayback();
        row.FemSourcePath = null;
    }

    // ── Play ─────────────────────────────────────────────────────────────

    private void PlayPrimary_Click(object? sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row?.PrimarySourcePath is null) return;

        if (_playingRow == row && _playingPrimary)
        {
            StopPlayback();
        }
        else
        {
            StopPlayback();
            _playingRow          = row;
            _playingPrimary      = true;
            row.IsPlayingPrimary = true;
            _player.Play(row.PrimarySourcePath);
        }
    }

    private void PlayFem_Click(object? sender, RoutedEventArgs e)
    {
        var row = RowOf(sender);
        if (row?.FemSourcePath is null) return;

        if (_playingRow == row && !_playingPrimary)
        {
            StopPlayback();
        }
        else
        {
            StopPlayback();
            _playingRow      = row;
            _playingPrimary  = false;
            row.IsPlayingFem = true;
            _player.Play(row.FemSourcePath);
        }
    }

    private void OnPlaybackStopped() => StopPlayback();

    private void StopPlayback()
    {
        _player.Stop();
        if (_playingRow is not null)
        {
            _playingRow.IsPlayingPrimary = false;
            _playingRow.IsPlayingFem     = false;
            _playingRow = null;
        }
    }

    // ── Cancel ───────────────────────────────────────────────────────────

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        if (_vm.IsImporting) _vm.Cancel();
        else                 Close();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static BatchVoRowViewModel? RowOf(object? sender)
        => (sender as Control)?.DataContext as BatchVoRowViewModel;

    private async Task<string?> PickVoFileAsync()
    {
        var options = new FilePickerOpenOptions
        {
            Title         = Loc.Get("VoImport_PickerTitle"),
            AllowMultiple = false,
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
}
