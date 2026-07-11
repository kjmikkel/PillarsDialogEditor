using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class TextTagValidationWindow : Window
{
    public TextTagValidationWindow() => InitializeComponent();

    public TextTagValidationWindow(TextTagValidationViewModel viewModel) : this()
        => DataContext = viewModel;
}
