using Avalonia;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;

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

    [AvaloniaFact]
    public void AllTokens_ResolveToBaseValues()
    {
        foreach (var (key, expected) in FontSizeTokens.BaseValues)
            Assert.Equal(expected, Size(key));
    }

    [AvaloniaFact]
    public void Micro_WasRetired_NoLongerDefined()
        => Assert.False(Application.Current!.TryGetResource("FontSize.Micro", Application.Current!.ActualThemeVariant, out _));
}
