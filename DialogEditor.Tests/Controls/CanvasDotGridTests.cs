using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Controls;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Controls;

// GitHub issue 225: a faint dot grid behind the canvas that pans and zooms with it.
public class CanvasDotGridTests
{
    public CanvasDotGridTests() => Loc.Configure(new AvaloniaStringProvider());

    [Fact]
    public void TileMatrix_AtHomeView_IsIdentity()
        => Assert.Equal(Matrix.Identity, CanvasDotGrid.TileMatrix(new Point(0, 0), 1.0));

    [Fact]
    public void TileMatrix_ScalesByZoom_ThenShiftsByTheScaledViewportOrigin()
    {
        // A graph point p is drawn at (p - location) * zoom, so the tile grid follows the same map.
        var m = CanvasDotGrid.TileMatrix(new Point(10, 5), 2.0);
        Assert.Equal(new Point(-20, -10), m.Transform(new Point(0, 0)));
        Assert.Equal(new Point(20, 30), m.Transform(new Point(20, 20)));
    }

    [Theory]
    [InlineData(1.0, true)]
    [InlineData(0.5, true)]
    [InlineData(0.3, false)]   // 6 px apart on screen: a grey haze, not a grid
    public void Drawn_OnlyWhileDotsStayApartOnScreen(double zoom, bool drawn)
        => Assert.Equal(drawn, CanvasDotGrid.IsDrawnAt(zoom));

    [AvaloniaFact]
    public void ConversationView_HasAGridBoundToTheEditor_InTheThemeColours()
    {
        var view = new ConversationView { DataContext = new ConversationViewModel(new StubDispatcher()) };
        var window = new Window { Width = 800, Height = 600, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var grid = window.GetVisualDescendants().OfType<CanvasDotGrid>().Single();
            Assert.NotNull(grid.Editor);
            Assert.True(Application.Current!.TryGetResource("Brush.Canvas.Grid", null, out var dot));
            Assert.True(Application.Current!.TryGetResource("Brush.Canvas.Background", null, out var bg));
            Assert.Equal(((ISolidColorBrush)dot!).Color, ((ISolidColorBrush)grid.DotBrush!).Color);
            Assert.Equal(((ISolidColorBrush)bg!).Color, ((ISolidColorBrush)grid.Background!).Color);
        }
        finally { window.Close(); }
    }
}
