using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class DiffWindow : Window
{
    private DiffHelpWindow? _helpWindow;

    public DiffWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
        if (!AppSettings.DiffWindowSeen)
        {
            AppSettings.DiffWindowSeen = true;
            IntroBanner.IsVisible = true;
        }
    }

    public DiffWindow(DiffViewModel vm) : this()
    {
        DataContext = vm;
    }

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        if (_helpWindow is null || !_helpWindow.IsVisible)
            _helpWindow = new DiffHelpWindow();
        _helpWindow.Show();
        _helpWindow.Activate();
    }

    private void IntroBanner_GotIt_Click(object? sender, RoutedEventArgs e)
        => IntroBanner.IsVisible = false;

    private void UndoBringIn_Click(object? sender, RoutedEventArgs e)
        => (DataContext as DiffViewModel)?.RequestUndoApply?.Invoke();
}
