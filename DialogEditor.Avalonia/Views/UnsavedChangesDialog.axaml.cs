using Avalonia;
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Views;

public enum UnsavedChangesResult { Save, Discard, Cancel }

public partial class UnsavedChangesDialog : Window
{
    public UnsavedChangesResult Result { get; private set; } = UnsavedChangesResult.Cancel;

    // Parameterless ctor so the XAML resource is reachable via the runtime loader (avoids AVLN3001).
    public UnsavedChangesDialog() => InitializeComponent();

    public UnsavedChangesDialog(string conversationName)
    {
        InitializeComponent();
        var template = Application.Current!.FindResource("UnsavedChanges_Message") as string
                       ?? "'{0}' has unsaved changes.";
        MessageBlock.Text = string.Format(template, conversationName);

        SaveButton.Click    += (_, _) => { Result = UnsavedChangesResult.Save;    Close(); };
        DiscardButton.Click += (_, _) => { Result = UnsavedChangesResult.Discard; Close(); };
        CancelButton.Click  += (_, _) => { Result = UnsavedChangesResult.Cancel;  Close(); };
    }
}
