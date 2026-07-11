using Avalonia.Controls;
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

    /// Shows modally over <paramref name="owner"/>; resolves to the user's choice.
    public async System.Threading.Tasks.Task<ScanDirtyChoice> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }
}
