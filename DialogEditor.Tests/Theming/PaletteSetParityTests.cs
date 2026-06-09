using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

public class PaletteSetParityTests
{
    [AvaloniaTheory]
    [InlineData("Palette.Light")]
    [InlineData("Palette.Colourblind")]
    [InlineData("Palette.HighContrast")]
    public void AlternatePaletteHasExactlySameKeysAsDark(string set)
    {
        var dark = PaletteHarness.Load("Palette.Dark").Keys.Cast<string>().ToHashSet();
        var alt = PaletteHarness.Load(set).Keys.Cast<string>().ToHashSet();

        Assert.True(dark.SetEquals(alt),
            $"{set} key set differs from Dark.\n" +
            $"Missing from {set}: {string.Join(", ", dark.Except(alt).OrderBy(k => k))}\n" +
            $"Extra in {set}: {string.Join(", ", alt.Except(dark).OrderBy(k => k))}");
    }
}
