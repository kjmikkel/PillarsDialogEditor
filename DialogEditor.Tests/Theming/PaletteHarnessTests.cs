using Avalonia.Media;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

public class PaletteHarnessTests
{
    [AvaloniaFact]
    public void WhiteOnBlackIsMaxContrast()
        => Assert.Equal(21.0, Wcag.ContrastRatio(Colors.White, Colors.Black), 1);

    [AvaloniaFact]
    public void SameColourIsMinContrast()
        => Assert.Equal(1.0, Wcag.ContrastRatio(Colors.White, Colors.White), 3);

    [AvaloniaFact]
    public void DarkPaletteLoadsAndResolvesAKnownPrimitive()
    {
        var dark = PaletteHarness.Load("Palette.Dark");
        Assert.Equal(Color.FromArgb(0xFF, 0x14, 0x14, 0x14),
                     PaletteHarness.Color(dark, "Palette.Neutral.80"));
    }
}
