using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// The clipping guards (issue 177) need real text measurement. The default headless renderer
/// measures every glyph at a fixed width whatever the font size, so text never "grows" with the
/// font scale and nothing could ever be found clipped. This pins the Skia-backed host.
/// </summary>
public class HeadlessTextMeasurementTests
{
    [AvaloniaFact]
    public void TextWidth_GrowsWithFontSize()
    {
        var small = new TextBlock { Text = "Hello", FontSize = 12 };
        var large = new TextBlock { Text = "Hello", FontSize = 24 };
        small.Measure(Size.Infinity);
        large.Measure(Size.Infinity);

        Assert.InRange(large.DesiredSize.Width / small.DesiredSize.Width, 1.8, 2.2);
    }
}
