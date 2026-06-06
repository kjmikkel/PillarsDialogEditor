using Avalonia.Controls;

namespace DialogEditor.Avalonia.Views;

public partial class ForceDeleteDialog : Window
{
    /// True if the user confirmed deletion, false if cancelled.
    public bool Result { get; private set; }

    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public ForceDeleteDialog() => InitializeComponent();

    /// <param name="message">The formatted warning message (branch name already interpolated by caller).</param>
    public ForceDeleteDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        ConfirmButton.Click += (_, _) => { Result = true;  Close(); };
        CancelButton.Click  += (_, _) => { Result = false; Close(); };
    }

    /// Shows modally over <paramref name="owner"/>; resolves to true if confirmed, false if cancelled.
    public async System.Threading.Tasks.Task<bool> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }
}
