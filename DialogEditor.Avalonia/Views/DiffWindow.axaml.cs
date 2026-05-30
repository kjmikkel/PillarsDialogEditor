using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class DiffWindow : Window
{
    public DiffWindow() => InitializeComponent();

    public DiffWindow(DiffViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
}
