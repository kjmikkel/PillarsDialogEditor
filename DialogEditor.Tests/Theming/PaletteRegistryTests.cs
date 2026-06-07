using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace DialogEditor.Tests.Theming;

public class PaletteRegistryTests
{
    private static Color Resolve(string key)
    {
        Assert.True(
            Application.Current!.TryGetResource(key, Application.Current!.ActualThemeVariant, out var v),
            $"Palette key '{key}' is not defined");
        return ((Color)v!);
    }

    [AvaloniaTheory]
    [InlineData("Palette.Neutral.80",  0xFF, 0x14, 0x14, 0x14)]
    [InlineData("Palette.Neutral.145", 0xFF, 0x25, 0x25, 0x25)]
    [InlineData("Palette.Neutral.200", 0xFF, 0x33, 0x33, 0x33)]
    [InlineData("Palette.Neutral.535", 0xFF, 0x88, 0x88, 0x88)]
    [InlineData("Palette.Neutral.910", 0xFF, 0xe8, 0xe8, 0xe8)]
    [InlineData("Palette.Crimson.700", 0xFF, 0x7b, 0x24, 0x1c)]
    [InlineData("Palette.Azure.600",   0xFF, 0x1a, 0x52, 0x76)]
    [InlineData("Palette.Green.550",   0xFF, 0x3a, 0x7a, 0x3a)]
    [InlineData("Palette.Amber.600",   0xFF, 0xc0, 0x8a, 0x2a)]
    [InlineData("Palette.Maroon.800",  0xFF, 0x7a, 0x2a, 0x2a)]
    [InlineData("Palette.Sky.250",     0xFF, 0x9c, 0xdc, 0xfe)]
    [InlineData("Palette.Sky.300",     0xFF, 0x9c, 0xc4, 0xff)]
    [InlineData("Palette.Cream.100",   0xFF, 0xff, 0xf8, 0xdc)]
    [InlineData("Palette.Amber.570",   0xFF, 0xe8, 0xd0, 0x80)] // bark footer, distinct from Amber.560
    [InlineData("Palette.Alpha.Scrim", 0xBB, 0x00, 0x00, 0x00)]
    public void PrimitiveResolvesToExpectedColor(string key, byte a, byte r, byte g, byte b)
        => Assert.Equal(Color.FromArgb(a, r, g, b), Resolve(key));

    [AvaloniaFact]
    public void AbsorbedAmber530_DoesNotExist()
        => Assert.False(
            Application.Current!.TryGetResource("Palette.Amber.530",
                Application.Current!.ActualThemeVariant, out _),
            "Amber.530 (#e0a030) was absorbed into Amber.540 per spec §6/§8 and must not be defined");
}
