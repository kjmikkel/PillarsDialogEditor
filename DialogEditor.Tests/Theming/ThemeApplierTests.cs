using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Styling;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Exercises the live palette swap against the shared headless <c>Application</c>. Because
/// that Application is an assembly-wide singleton (other [AvaloniaFact] tests assert Dark
/// palette values), every test restores Dark in a finally block.
/// </summary>
public class ThemeApplierTests
{
    private static Color Resolve(string key)
    {
        var app = Application.Current!;
        Assert.True(app.TryGetResource(key, app.ActualThemeVariant, out var v),
            $"token '{key}' did not resolve");
        return ((ISolidColorBrush)v!).Color;
    }

    [Fact]
    public void Available_ListsThePalettesInOrder()
    {
        var ids = new ThemeApplier().Available.Select(o => o.Id);
        Assert.Equal(["Dark", "Light", "Colourblind", "HighContrast"], ids);
    }

    [AvaloniaFact]
    public void Apply_Light_RetintsTokens()
    {
        var dark = Resolve("Brush.Surface.Window");
        try
        {
            new ThemeApplier().Apply("Light");
            // If only the palette were swapped (not Tokens reloaded), the already-built
            // Brush.* SolidColorBrushes would keep Dark's colour and this would be equal.
            Assert.NotEqual(dark, Resolve("Brush.Surface.Window"));
        }
        finally { new ThemeApplier().Apply("Dark"); }
    }

    [AvaloniaFact]
    public void Apply_Light_SetsLightVariant()
    {
        try
        {
            new ThemeApplier().Apply("Light");
            Assert.Equal(ThemeVariant.Light, Application.Current!.RequestedThemeVariant);
        }
        finally { new ThemeApplier().Apply("Dark"); }
    }

    [AvaloniaFact]
    public void Apply_HighContrast_KeepsDarkVariant()
    {
        try
        {
            new ThemeApplier().Apply("HighContrast");
            Assert.Equal(ThemeVariant.Dark, Application.Current!.RequestedThemeVariant);
        }
        finally { new ThemeApplier().Apply("Dark"); }
    }

    [AvaloniaFact]
    public void Apply_BumpsRevision()
    {
        try
        {
            var before = ThemeService.Current.Revision;
            new ThemeApplier().Apply("Colourblind");
            Assert.Equal(before + 1, ThemeService.Current.Revision);
        }
        finally { new ThemeApplier().Apply("Dark"); }
    }

    [AvaloniaFact]
    public void Apply_ThenDark_RestoresOriginalColour()
    {
        var dark = Resolve("Brush.Surface.Window");
        new ThemeApplier().Apply("Light");
        new ThemeApplier().Apply("Dark");
        Assert.Equal(dark, Resolve("Brush.Surface.Window"));
    }

    [Theory]
    [InlineData(PlatformThemeVariant.Dark,  ColorContrastPreference.NoPreference, "Dark")]
    [InlineData(PlatformThemeVariant.Light, ColorContrastPreference.NoPreference, "Light")]
    [InlineData(PlatformThemeVariant.Dark,  ColorContrastPreference.High,         "HighContrast")]
    [InlineData(PlatformThemeVariant.Light, ColorContrastPreference.High,         "HighContrast")]
    public void DetectOsThemeId_MapsPlatformPreferences(
        PlatformThemeVariant variant, ColorContrastPreference contrast, string expectedId)
    {
        var values = new PlatformColorValues { ThemeVariant = variant, ContrastPreference = contrast };
        Assert.Equal(expectedId, ThemeApplier.DetectOsThemeId(values));
    }

    [Fact]
    public void DetectOsThemeId_NullValues_FallsBackToDark()
    {
        Assert.Equal("Dark", ThemeApplier.DetectOsThemeId(null));
    }
}
