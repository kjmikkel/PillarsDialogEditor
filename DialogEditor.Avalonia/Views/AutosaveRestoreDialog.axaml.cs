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

    /// <param name="fallbackFolder">Where the copy was found when it is the per-user fallback copy
    /// (issue 37), else null. The user's mental model is "next to my project", so a copy from
    /// elsewhere says where it came from.</param>
    public AutosaveRestoreDialog(DateTime sidecarLocalTime, string? fallbackFolder = null) : this()
    {
        MessageText.Text = Loc.Format("AutosaveRestore_Message",
            sidecarLocalTime.ToString("g"));
        if (fallbackFolder is not null)
            MessageText.Text += Environment.NewLine + Environment.NewLine
                              + Loc.Format("AutosaveRestore_FromFallback", fallbackFolder);
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
