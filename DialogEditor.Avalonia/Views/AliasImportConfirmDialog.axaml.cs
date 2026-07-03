using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Views;

// Task 9: modal confirmation shown before importing over a node whose ExternalVO
// aliases another node's recording (see Task 8's VoAliasImportPrompt/Choice).
// Cancel is the IsDefault button (see XAML comment) so an accidental Enter never
// clobbers audio shared by other nodes.
public partial class AliasImportConfirmDialog : Window
{
    public VoAliasImportChoice Choice { get; private set; } = VoAliasImportChoice.Cancel;

    public AliasImportConfirmDialog() => InitializeComponent();   // XAML previewer

    public AliasImportConfirmDialog(VoAliasImportPrompt prompt) : this()
        => MessageText.Text = Loc.Format("AliasImport_Message",
            prompt.TargetPath, prompt.SharedWithOthers);

    private void Overwrite_Click(object? s, RoutedEventArgs e)
        { Choice = VoAliasImportChoice.OverwriteShared; Close(); }
    private void ClearAndOwn_Click(object? s, RoutedEventArgs e)
        { Choice = VoAliasImportChoice.ClearAliasImportOwn; Close(); }
    private void Cancel_Click(object? s, RoutedEventArgs e)
        { Choice = VoAliasImportChoice.Cancel; Close(); }
}
