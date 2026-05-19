using Avalonia;
using Avalonia.Controls;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class LegendWindow : Window
{
    // Invoked when the OS close button hides the window, so MainWindow can
    // uncheck the ? toggle button.
    public Action? OnHidden { get; set; }

    public LegendWindow() => InitializeComponent();

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        e.Cancel = true;
        AppSettings.SetLegendPosition(Position.X, Position.Y);
        Hide();
        OnHidden?.Invoke();
    }

    public void ShowAndRestore(Window owner)
    {
        if (AppSettings.GetLegendPosition() is var (x, y))
            Position = new PixelPoint((int)x, (int)y);
        Show(owner);
    }

    public void HideAndSave()
    {
        AppSettings.SetLegendPosition(Position.X, Position.Y);
        Hide();
    }
}
