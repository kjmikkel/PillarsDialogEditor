using Avalonia;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Exercises FontScaleApplier against the shared headless Application. Because that
/// Application is an assembly-wide singleton (FontSizeTokenTests asserts base values),
/// every test restores scale 1.0 in a finally block.
/// </summary>
public class FontScaleApplierTests
{
    private static double Size(string key)
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource(key, app.ActualThemeVariant, out var v),
            $"FontSize key '{key}' did not resolve");
        return Assert.IsType<double>(v);
    }

    [AvaloniaFact]
    public void Apply_ScalesAllFontSizeTokens()
    {
        try
        {
            new FontScaleApplier().Apply(1.25);
            foreach (var (key, baseValue) in FontSizeTokens.BaseValues)
                Assert.Equal(baseValue * 1.25, Size(key));
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void Apply_OneScale_RestoresBaseValues()
    {
        new FontScaleApplier().Apply(1.5);
        new FontScaleApplier().Apply(1.0);
        foreach (var (key, baseValue) in FontSizeTokens.BaseValues)
            Assert.Equal(baseValue, Size(key));
    }

    [AvaloniaFact]
    public void Apply_BumpsServiceRevision()
    {
        var before = FontScaleService.Current.Revision;
        try
        {
            new FontScaleApplier().Apply(1.25);
            Assert.True(FontScaleService.Current.Revision > before);
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }
}
