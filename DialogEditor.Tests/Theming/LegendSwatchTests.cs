using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Views;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Issue 180: the legend's node-type swatches showed only the card body colour, which is pastel
/// in Light (hard to see against the window) and plain white for every type in High Contrast. The
/// canvas tells node types apart by their header strip, so each swatch now draws a small node:
/// header on top, body below, inside an outline.
/// </summary>
public class LegendSwatchTests
{
    private static readonly string[] NodeTypes = ["Npc", "Player", "Narrator", "Script"];

    private static Color Token(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, null, out var r), key);
        return ((ISolidColorBrush)r!).Color;
    }

    private static Color ColorOf(IBrush? b) => Assert.IsAssignableFrom<ISolidColorBrush>(b).Color;

    [AvaloniaFact]
    public void NodeSwatches_DrawTheCanvasHeaderAndBody()
    {
        var w = new LegendWindow();
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            var swatches = w.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("nodeSwatch")).ToList();
            Assert.Equal(NodeTypes.Length, swatches.Count);

            for (var i = 0; i < NodeTypes.Length; i++)
            {
                var swatch = swatches[i];
                Assert.Equal(Token("Brush.Border.Strong"), ColorOf(swatch.BorderBrush));
                Assert.Equal(new Thickness(1), swatch.BorderThickness);

                var parts = swatch.GetVisualDescendants().OfType<Border>().ToList();
                Assert.Equal(2, parts.Count);
                Assert.Equal(Token($"Brush.Node.{NodeTypes[i]}.Header"), ColorOf(parts[0].Background));
                Assert.Equal(Token($"Brush.Node.{NodeTypes[i]}.Body"),   ColorOf(parts[1].Background));
            }
        }
        finally { w.Close(); }
    }

    // The primitives behind Brush.Node.{Npc,Player,Narrator,Script}.Header (Tokens.axaml).
    private static readonly string[] HeaderPrimitives =
        ["Palette.Crimson.700", "Palette.Azure.600", "Palette.Teal.600", "Palette.Slate.700"];

    [AvaloniaTheory]
    [InlineData("Palette.Dark")]
    [InlineData("Palette.Light")]
    [InlineData("Palette.Colourblind")]
    [InlineData("Palette.HighContrast")]
    public void NodeHeaders_DifferInEveryTheme(string set)
    {
        // The header strip is what tells the swatches (and the canvas nodes) apart, so no two
        // node types may share a header colour in any theme.
        var dict = PaletteHarness.Load(set);
        var colours = HeaderPrimitives.Select(k => PaletteHarness.Color(dict, k)).ToList();
        Assert.Equal(colours.Count, colours.Distinct().Count());
    }
}
