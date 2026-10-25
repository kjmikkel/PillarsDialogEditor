using Avalonia;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Issue 178: table column widths are ColumnWidth.* tokens that scale with the font scale, so a
/// table's columns grow with their text instead of overlapping at large scales.
/// </summary>
public class ColumnWidthTokenTests
{
    private static double Resource(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, null, out var r), key);
        return (double)r!;
    }

    [AvaloniaFact]
    public void TokensAxaml_DeclaresEveryBaseValue()
    {
        foreach (var (key, value) in ColumnWidthTokens.BaseValues)
            Assert.Equal(value, Resource(key));
    }

    /// <summary>
    /// GitHub issue 261: Dock.Avalonia sizes its pane buttons (Pane options, Pin, Close) from
    /// these two keys, and their glyphs fill the button. Left to the theme they stayed 18x16
    /// at every font scale; scaling them with the column widths makes the glyphs grow too.
    /// </summary>
    [Theory]
    [InlineData("DockChromeButtonWidth", 18)]
    [InlineData("DockChromeButtonHeight", 16)]
    public void DockPaneButtonSizes_ScaleWithTheFont(string key, double baseValue)
    {
        Assert.True(ColumnWidthTokens.BaseValues.TryGetValue(key, out var value), key);
        Assert.Equal(baseValue, value);
    }

    [AvaloniaFact]
    public void Apply_ScalesEveryColumnWidth()
    {
        try
        {
            new FontScaleApplier().Apply(2.0);
            foreach (var (key, value) in ColumnWidthTokens.BaseValues)
                Assert.Equal(value * 2.0, Resource(key));
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void ThemeSwitch_KeepsColumnWidthsScaled()
    {
        // ThemeApplier swaps the token dictionaries and then re-applies the font scale; the
        // column widths must come back scaled, not at their base values.
        try
        {
            new FontScaleApplier().Apply(2.0);
            new ThemeApplier().Apply("Light");
            Assert.Equal(ColumnWidthTokens.BaseValues["ColumnWidth.Conversation"] * 2.0,
                         Resource("ColumnWidth.Conversation"));
        }
        finally
        {
            new ThemeApplier().Apply("Dark");
            new FontScaleApplier().Apply(1.0);
        }
    }
}
