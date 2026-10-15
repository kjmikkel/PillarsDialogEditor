using Avalonia;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Core.Layout;

namespace DialogEditor.Tests.Theming;

/// Issue 82: the canvas card's sizes are Canvas.* tokens pinned to CardMetrics. Font scale must
/// NOT change them: on the canvas it magnifies the zoom instead, so scaling the tokens too would
/// scale the canvas twice.
public class CanvasTokenTests
{
    private static readonly Dictionary<string, double> Expected = new()
    {
        ["Canvas.CardWidth"]         = CardMetrics.Width,
        ["Canvas.FontSize.Header"]   = CardMetrics.HeaderFontSize,
        ["Canvas.FontSize.Body"]     = CardMetrics.BodyFontSize,
        ["Canvas.FontSize.Footer"]   = CardMetrics.FooterFontSize,
        ["Canvas.FontSize.Badge"]    = 10,
        ["Canvas.LineHeight.Header"] = CardMetrics.HeaderLineHeight,
        ["Canvas.LineHeight.Body"]   = CardMetrics.BodyLineHeight,
        ["Canvas.LineHeight.Footer"] = CardMetrics.FooterLineHeight,
    };

    private static double Resource(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, null, out var r), key);
        return (double)r!;
    }

    [AvaloniaFact]
    public void TokensAxaml_MatchesCardMetrics()
    {
        foreach (var (key, value) in Expected) Assert.Equal(value, Resource(key));
    }

    [AvaloniaFact]
    public void FontScale_LeavesCanvasTokensUnscaled()
    {
        try
        {
            new FontScaleApplier().Apply(2.0);
            foreach (var (key, value) in Expected) Assert.Equal(value, Resource(key));
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void ThemeSwitch_LeavesCanvasTokensUnscaled()
    {
        try
        {
            new FontScaleApplier().Apply(2.0);
            new ThemeApplier().Apply("Light");
            foreach (var (key, value) in Expected) Assert.Equal(value, Resource(key));
        }
        finally
        {
            new ThemeApplier().Apply("Dark");
            new FontScaleApplier().Apply(1.0);
        }
    }
}
