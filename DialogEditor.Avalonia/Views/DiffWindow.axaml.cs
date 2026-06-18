using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class DiffWindow : Window
{
    private DiffHelpWindow? _helpWindow;

    public DiffWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public DiffWindow(DiffViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        if (_helpWindow is null || !_helpWindow.IsVisible)
            _helpWindow = new DiffHelpWindow();
        _helpWindow.Show();
        _helpWindow.Activate();
    }

    private void UndoBringIn_Click(object? sender, RoutedEventArgs e)
        => (DataContext as DiffViewModel)?.RequestUndoApply?.Invoke();
}
