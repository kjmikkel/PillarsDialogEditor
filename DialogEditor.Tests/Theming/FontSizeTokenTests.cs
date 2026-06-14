using Avalonia;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

public class FontSizeTokenTests
{
    private static double Size(string key)
    {
        Assert.True(
            Application.Current!.TryGetResource(key, Application.Current!.ActualThemeVariant, out var v),
            $"FontSize key '{key}' is not defined");
        return Assert.IsType<double>(v);
    }

    [AvaloniaTheory]
    [InlineData("FontSize.Caption", 9)]
    [InlineData("FontSize.Small", 10)]
    [InlineData("FontSize.Label", 11)]
    [InlineData("FontSize.Body", 12)]
    [InlineData("FontSize.Medium", 13)]
    [InlineData("FontSize.Subtitle", 14)]
    [InlineData("FontSize.Title", 18)]
    [InlineData("FontSize.Display", 32)]
    public void TokenResolvesToExpectedValue(string key, double expected)
        => Assert.Equal(expected, Size(key));

    [AvaloniaFact]
    public void Micro_WasRetired_NoLongerDefined()
        => Assert.False(Application.Current!.TryGetResource("FontSize.Micro", Application.Current!.ActualThemeVariant, out _));
}
