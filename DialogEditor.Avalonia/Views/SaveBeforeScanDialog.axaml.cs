using Avalonia.Controls;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

/// Three-way consent shown when starting a project-wide scan while the project has
/// unsaved changes (the scan reads saved state only). Closing the window without a
/// button press counts as Cancel.
public partial class SaveBeforeScanDialog : Window
{
    public ScanDirtyChoice Result { get; private set; } = ScanDirtyChoice.Cancel;

    public SaveBeforeScanDialog()
    {
        InitializeComponent();
        SaveAndScanButton.Click   += (_, _) => { Result = ScanDirtyChoice.SaveAndScan;   Close(); };
        ScanSavedOnlyButton.Click += (_, _) => { Result = ScanDirtyChoice.ScanSavedOnly; Close(); };
        CancelButton.Click        += (_, _) => { Result = ScanDirtyChoice.Cancel;        Close(); };
    }

    /// Overload for callers that need scan-specific copy (e.g. the Speaker Line Browser).
    /// Null keys keep the default XAML resources (the Validate Text Tags wording). Cancel
    /// stays generic. The two action buttons map to SaveAndScan / ScanSavedOnly.
    public SaveBeforeScanDialog(string? messageKey, string? saveButtonKey, string? proceedButtonKey)
        : this()
    {
        if (messageKey is not null)       MessageText.Text            = Loc.Get(messageKey);
        if (saveButtonKey is not null)    SaveAndScanButton.Content   = Loc.Get(saveButtonKey);
        if (proceedButtonKey is not null) ScanSavedOnlyButton.Content = Loc.Get(proceedButtonKey);
    }

    /// Shows modally over <paramref name="owner"/>; resolves to the user's choice.
    public async System.Threading.Tasks.Task<ScanDirtyChoice> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }
}
