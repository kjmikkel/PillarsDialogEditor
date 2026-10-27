using Avalonia;
using Avalonia.Controls;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class ModeChooserWindow : Window
{
    // Below this window width (at 100 % text size) the two panes no longer fit side by side.
    // Scaled with the text size, since the bullets need proportionally more room.
    private const double StackBelowWidth = 560;

    private readonly ModeChooserViewModel _viewModel = new();
    private bool? _stacked;

    public ModeChooserWindow()
    {
        InitializeComponent();
        DataContext = _viewModel;
        _viewModel.CloseRequested += Close;
        Closed += (_, _) => _viewModel.OnClosedWithoutChoice();
        // SizeChanged, not Width: the window can be resized, and WindowSizing may cap it to the screen.
        SizeChanged += (_, e) => ApplyLayout(e.NewSize.Width);
    }

    private void ApplyLayout(double width)
    {
        if (width <= 0) return;
        var stacked = width < StackBelowWidth * FontScaleService.Current.Scale;
        if (stacked == _stacked) return;
        _stacked = stacked;

        PanesGrid.ColumnDefinitions[1].Width = stacked ? new GridLength(0) : GridLength.Star;
        Grid.SetColumn(EditingPane, stacked ? 0 : 1);
        Grid.SetRow(EditingPane, stacked ? 1 : 0);
        ReadingPane.Margin = stacked ? new Thickness(0, 0, 0, 6) : new Thickness(0, 0, 8, 0);
        EditingPane.Margin = stacked ? new Thickness(0, 6, 0, 0) : new Thickness(8, 0, 0, 0);
    }
}
