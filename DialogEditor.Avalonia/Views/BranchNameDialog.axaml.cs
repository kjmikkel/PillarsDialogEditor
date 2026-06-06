using Avalonia.Controls;

namespace DialogEditor.Avalonia.Views;

public partial class BranchNameDialog : Window
{
    /// The trimmed branch name if OK was clicked, null if cancelled or blank.
    public string? Result { get; private set; }

    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public BranchNameDialog() => InitializeComponent();

    /// <param name="title">Window title from Strings (NewTitle vs RenameTitle).</param>
    /// <param name="prefill">Current branch name for rename, null for new.</param>
    public BranchNameDialog(string title, string? prefill)
    {
        InitializeComponent();
        Title = title;
        NameBox.Text = prefill ?? "";
        OkButton.Click     += (_, _) => Accept();
        CancelButton.Click += (_, _) => { Result = null; Close(); };
        Opened             += (_, _) => NameBox.Focus();
    }

    private void Accept()
    {
        Result = string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim();
        Close();
    }

    /// Shows modally over <paramref name="owner"/>; resolves to the branch name, or null if cancelled.
    public async System.Threading.Tasks.Task<string?> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }
}
