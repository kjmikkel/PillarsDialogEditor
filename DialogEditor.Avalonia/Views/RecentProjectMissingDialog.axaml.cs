using Avalonia.Controls;

namespace DialogEditor.Avalonia.Views;

public partial class RecentProjectMissingDialog : Window
{
    /// True = remove the entry from the recent list; false = keep it.
    public bool Result { get; private set; }

    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public RecentProjectMissingDialog() => InitializeComponent();

    /// <param name="message">The formatted message (path already interpolated by caller).</param>
    public RecentProjectMissingDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        RemoveButton.Click += (_, _) => { Result = true;  Close(); };
        KeepButton.Click   += (_, _) => { Result = false; Close(); };
    }

    /// Shows modally over <paramref name="owner"/>; resolves to true if the user removed the entry.
    public async System.Threading.Tasks.Task<bool> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }
}
