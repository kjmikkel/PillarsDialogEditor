using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class VoImportDialog : Window
{
    private readonly IVoImporter _importer = null!;

    private string? _primarySource;
    private string? _femSource;

    /// Set to non-null when the user clicks OK.
    public VoImportDialogResult? Result { get; private set; }

    // Parameterless ctor so the XAML compiler can embed the type (avoids AVLN3001).
    public VoImportDialog() => InitializeComponent();

    public VoImportDialog(IVoImporter importer, VoImportPaths paths)
    {
        InitializeComponent();
        _importer = importer;

        // Show destination paths as hints in the labels (dimmed placeholder)
        PrimaryLabel.Text = Path.GetFileName(paths.PrimaryDestinationPath);
        if (paths.FemDestinationPath is not null)
            FemLabel.Text = Path.GetFileName(paths.FemDestinationPath);
    }

    private async void BrowsePrimary_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickVoFileAsync();
        if (path is null) return;
        _primarySource               = path;
        PrimaryLabel.Text            = Path.GetFileName(path);
        ClearPrimaryButton.IsVisible = true;
        UpdateWwiseWarning();
        UpdateOkButton();
    }

    private void ClearPrimary_Click(object? sender, RoutedEventArgs e)
    {
        _primarySource               = null;
        PrimaryLabel.Text            = "—";
        ClearPrimaryButton.IsVisible = false;
        UpdateWwiseWarning();
        UpdateOkButton();
    }

    private async void BrowseFem_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickVoFileAsync();
        if (path is null) return;
        _femSource            = path;
        FemLabel.Text         = Path.GetFileName(path);
        ClearFemButton.IsVisible = true;
        UpdateWwiseWarning();
    }

    private void ClearFem_Click(object? sender, RoutedEventArgs e)
    {
        _femSource               = null;
        FemLabel.Text            = "—";
        ClearFemButton.IsVisible = false;
        UpdateWwiseWarning();
    }

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (_primarySource is null) return;
        Result = new VoImportDialogResult(_primarySource, _femSource);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void DownloadWwise_Click(object? sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://www.audiokinetic.com/en/products/wwise/") { UseShellExecute = true });

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
        // OK enabled when primary is set AND (it's a .wem OR Wwise is available).
        var isWav = _primarySource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true;
        OkButton.IsEnabled = _primarySource is not null && (!isWav || _importer.IsWwiseAvailable);
    }

    private void UpdateWwiseWarning()
    {
        // Show warning if any selected file is .wav and Wwise is absent.
        var anyWav =
            (_primarySource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true) ||
            (_femSource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true);
        WwiseWarningPanel.IsVisible = anyWav && !_importer.IsWwiseAvailable;
    }
}
