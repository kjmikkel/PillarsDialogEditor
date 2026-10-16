using Avalonia.Controls;
using Avalonia.Controls.Templates;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

/// Offers to restore a crash-recovery autosave found at project open.
/// Result: the copy to restore; null (incl. closing the window) = discard the offer,
/// which deletes the copies per spec 2026-07-12 §4.
public partial class AutosaveRestoreDialog : Window
{
    public AutosaveGeneration? Result { get; private set; }

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

        RestoreButton.Click += (_, _) => { Result = CopyList.SelectedItem as AutosaveGeneration ?? newest; Close(); };
        DiscardButton.Click += (_, _) => { Result = null; Close(); };
    }

    private static string Label(AutosaveGeneration g)
    {
        var time = g.TimeUtc.ToLocalTime().ToString("g");
        return g.IsFallback ? Loc.Format("AutosaveRestore_CopyInEditorFolder", time) : time;
    }

    /// Shows modally over <paramref name="owner"/>; resolves to the copy to restore, or null.
    public async System.Threading.Tasks.Task<AutosaveGeneration?> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }
}
