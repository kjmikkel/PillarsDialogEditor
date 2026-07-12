using Avalonia.Controls;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Views;

/// Offers to restore a crash-recovery autosave found at project open.
/// Result: true = restore; false (incl. closing the window) = discard the offer,
/// which deletes the sidecar per spec 2026-07-12 §4.
public partial class AutosaveRestoreDialog : Window
{
    public bool Result { get; private set; }

    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public AutosaveRestoreDialog() => InitializeComponent();

    public AutosaveRestoreDialog(DateTime sidecarLocalTime) : this()
    {
        MessageText.Text = Loc.Format("AutosaveRestore_Message",
            sidecarLocalTime.ToString("g"));
        RestoreButton.Click += (_, _) => { Result = true;  Close(); };
        DiscardButton.Click += (_, _) => { Result = false; Close(); };
    }

    /// Shows modally over <paramref name="owner"/>; resolves to true if restoring.
    public async System.Threading.Tasks.Task<bool> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }
}
