using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class CommitConsentDialog : Window
{
    /// The commit message if the user clicked Commit, null if cancelled.
    public string? Result { get; private set; }

    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public CommitConsentDialog() => InitializeComponent();

    public CommitConsentDialog(PendingCommit pending)
    {
        InitializeComponent();
        FileList.ItemsSource = pending.Files;
        MessageBox.Text = pending.DefaultMessage;
        CommitButton.Click += (_, _) => Commit();
        CancelButton.Click += (_, _) => { Result = null; Close(); };
        Opened += (_, _) => MessageBox.Focus();
    }

    private void Commit()
    {
        Result = string.IsNullOrWhiteSpace(MessageBox.Text) ? null : MessageBox.Text.Trim();
        Close();
    }

    /// Shows modally over <paramref name="owner"/>; resolves to the commit message, or null if cancelled.
    public async System.Threading.Tasks.Task<string?> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }
}
