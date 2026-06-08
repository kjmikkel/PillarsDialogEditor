using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Loads a single Palette.&lt;set&gt;.axaml as an isolated ResourceDictionary and reads its
/// Color primitives. Palette files contain only &lt;Color&gt;/&lt;BoxShadows&gt; (no StaticResource),
/// so they load standalone with no need for Tokens.axaml. Layer 1 tests run entirely at the
/// primitive level (spec §5). Data-driven over AllSets so future palettes are one list entry.
/// </summary>
internal static class PaletteHarness
{
    private const string ResDir = "avares://DialogEditor.Avalonia.Shared/Resources/";

    // The default first; the three Layer 1 alternates follow. A new palette = one entry here.
    public static readonly string[] AllSets =
        { "Palette.Dark", "Palette.Light", "Palette.HighContrast", "Palette.Colourblind" };

    // The sets the accessibility contract is enforced on (Dark is the grandfathered baseline, §5).
    public static readonly string[] EnforcedSets =
        { "Palette.Light", "Palette.HighContrast", "Palette.Colourblind" };

    public static ResourceDictionary Load(string set)
        => (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(ResDir + set + ".axaml"));

    public static Color Color(ResourceDictionary dict, string key)
    {
        Assert.True(dict.TryGetResource(key, null, out var value),
            $"Palette key '{key}' is not defined");
        return Assert.IsType<Color>(value);
    }
}
