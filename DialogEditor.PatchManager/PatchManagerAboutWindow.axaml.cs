using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.PatchManager;

public partial class PatchManagerAboutWindow : Window
{
    public PatchManagerAboutWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public PatchManagerAboutWindow(PatchManagerAboutViewModel viewModel) : this()
        => DataContext = viewModel;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
