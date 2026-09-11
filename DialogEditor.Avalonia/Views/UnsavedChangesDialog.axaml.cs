using Avalonia.Controls;
using DialogEditor.ViewModels.Resources;

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
        // See ConversationNameDialog: no English fallback. The one that used to sit here
        // had also fallen behind the resource, which had gained a second paragraph.
        MessageBlock.Text = Loc.Format("UnsavedChanges_Message", conversationName);

        SaveButton.Click    += (_, _) => { Result = UnsavedChangesResult.Save;    Close(); };
        DiscardButton.Click += (_, _) => { Result = UnsavedChangesResult.Discard; Close(); };
        CancelButton.Click  += (_, _) => { Result = UnsavedChangesResult.Cancel;  Close(); };
    }
}
