using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class TextTagValidationWindow : Window
{
    public TextTagValidationWindow() => InitializeComponent();

    public TextTagValidationWindow(TextTagValidationViewModel viewModel) : this()
        => DataContext = viewModel;

    /// A base-game read can take seconds; closing the window must not leave it running
    /// against a VM nobody can see.
    protected override void OnClosed(EventArgs e)
    {
        (DataContext as TextTagValidationViewModel)?.Cancel();
        base.OnClosed(e);
    }
}
