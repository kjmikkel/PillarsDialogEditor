using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class PatchManagerWindow : Window
{
    public PatchManagerWindow() => InitializeComponent();

    public PatchManagerWindow(PatchManagerViewModel vm) : this()
        => DataContext = vm;
}
