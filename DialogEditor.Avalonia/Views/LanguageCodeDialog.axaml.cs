using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DialogEditor.Avalonia.Views;

public partial class LanguageCodeDialog : Window
{
    public string? Result { get; private set; }

    public LanguageCodeDialog() : this(null) { }

    public LanguageCodeDialog(string? initialValue)
    {
        InitializeComponent();
        LanguageCodeBox.Text = initialValue ?? string.Empty;

        CancelButton.Click += (_, _) =>
        {
            Result = null;
            Close();
        };

        Opened += (_, _) => LanguageCodeBox.Focus();

        LanguageCodeBox.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                AcceptAndClose();
                e.Handled = true;
            }
            else if (e.Key == Key.Escape)
            {
                Result = null;
                Close();
                e.Handled = true;
            }
        };
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
        => AcceptAndClose();

    private void AcceptAndClose()
    {
        var trimmed = LanguageCodeBox.Text?.Trim();
        Result = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        Close();
    }
}
