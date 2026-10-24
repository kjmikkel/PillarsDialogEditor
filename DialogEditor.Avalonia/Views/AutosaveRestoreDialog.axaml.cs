using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

/// Offers to restore a crash-recovery autosave found at project open.
/// Result: restore a copy, discard them all, or decide later. Later is the default, so Esc
/// and closing the window keep every copy (issue 236); Discard deletes them (spec 2026-07-12
/// §4) and only after a second, explicit confirmation.
public partial class AutosaveRestoreDialog : Window
{
    public AutosaveDecision Result { get; private set; } = AutosaveDecision.Later;

    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public AutosaveRestoreDialog() => InitializeComponent();

    /// <param name="generations">Every recovery copy, newest first (issue 36). The message
    /// describes the newest; earlier ones sit in a collapsed picker. A copy from the per-user
    /// fallback folder (issue 37) says where it came from, because the user's mental model is
    /// "next to my project".</param>
    public AutosaveRestoreDialog(IReadOnlyList<AutosaveGeneration> generations) : this()
    {
        var newest = generations[0];
        MessageText.Text = Loc.Format("AutosaveRestore_Message", newest.TimeUtc.ToLocalTime().ToString("g"));
        if (newest.IsFallback)
            MessageText.Text += Environment.NewLine + Environment.NewLine
                              + Loc.Format("AutosaveRestore_FromFallback", Path.GetDirectoryName(newest.Path) ?? newest.Path);

        CopyList.ItemTemplate = new FuncDataTemplate<AutosaveGeneration>((g, _) => new TextBlock { Text = Label(g) });
        CopyList.ItemsSource = generations;
        CopyList.SelectedItem = newest;
        EarlierCopies.IsVisible = generations.Count > 1;

        RestoreButton.Click += (_, _) => { Result = AutosaveDecision.Restore(CopyList.SelectedItem as AutosaveGeneration ?? newest); Close(); };
        LaterButton.Click   += (_, _) => { Result = AutosaveDecision.Later; Close(); };
        // Discard only opens the confirmation; focus lands on Keep, so a stray Enter or Space
        // after the click still can't delete anything.
        DiscardButton.Click += (_, _) => ShowDiscardConfirmation(true);
        KeepButton.Click    += (_, _) => ShowDiscardConfirmation(false);
        ConfirmDiscardButton.Click += (_, _) => { Result = AutosaveDecision.Discard; Close(); };
    }

    private void ShowDiscardConfirmation(bool show)
    {
        ConfirmDiscardPanel.IsVisible = show;
        DiscardButton.IsEnabled = !show;
        (show ? KeepButton : DiscardButton).Focus();
    }

    private static string Label(AutosaveGeneration g)
    {
        var time = g.TimeUtc.ToLocalTime().ToString("g");
        return g.IsFallback ? Loc.Format("AutosaveRestore_CopyInEditorFolder", time) : time;
    }

    /// Shows modally over <paramref name="owner"/>; resolves to the user's decision.
    public async System.Threading.Tasks.Task<AutosaveDecision> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }
}
