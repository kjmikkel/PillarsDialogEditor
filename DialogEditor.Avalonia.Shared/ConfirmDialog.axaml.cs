using Avalonia.Automation;
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Shared;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() => InitializeComponent();   // XAML previewer

    /// All text arrives already localised; <paramref name="details"/> (e.g. file names) is
    /// shown in a scrolling list when non-empty. With <paramref name="showCancel"/> false it is a
    /// one-button message (GitHub issue 62: "newer file format"); the button then also answers Esc.
    public ConfirmDialog(string title, string message, string confirmText, IReadOnlyList<string>? details,
                         bool showCancel = true)
        : this()
    {
        Title                   = title;
        MessageText.Text        = message;
        ConfirmButton.Content   = confirmText;
        AutomationProperties.SetName(ConfirmButton, confirmText);
        if (details is { Count: > 0 })
        {
            DetailsList.ItemsSource = details;
            DetailsBorder.IsVisible = true;
        }
        if (!showCancel)
        {
            CancelButton.IsVisible = false;
            ConfirmButton.IsCancel = true;
        }
        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click  += (_, _) => Close(false);
    }

    /// Modal over <paramref name="owner"/>; true when the user confirmed.
    public async Task<bool> ShowAsync(Window owner) => await ShowDialog<bool>(owner);
}
