using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Platform;
using Avalonia.Styling;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Layer 2 runtime palette switching. Swaps the merged palette + <c>Tokens.axaml</c>
/// dictionaries on the live <see cref="Application"/> and flips the FluentTheme variant, so
/// the whole UI retints without a restart.
///
/// Why both dictionaries are replaced: <c>Tokens.axaml</c> references palette colours with
/// <c>{StaticResource Palette.*}</c>, which resolves once at load. Replacing only the palette
/// leaves the already-built <c>Brush.*</c> brushes on their old colours, so Tokens must be
/// reloaded after the new palette. It is reloaded as a sibling of the palette inside one
/// wrapper dictionary (mirroring App.axaml's layout) so its StaticResource refs resolve.
/// </summary>
public sealed class ThemeApplier : IThemeApplier
{
    private const string ResDir = "avares://DialogEditor.Avalonia.Shared/Resources/";

    private sealed record Entry(string Id, string PaletteFile, ThemeVariant Variant, string DisplayNameKey);

    // The single add-point for a new palette (Layer 1 §8: never bake in "there are four").
    // Light is the only set that maps to the FluentTheme Light variant; the others are
    // dark-valued surfaces, so their base controls follow the Dark variant.
    private static readonly Entry[] Catalog =
    [
        new("Dark",         "Palette.Dark",         ThemeVariant.Dark,  "Theme_Name_Dark"),
        new("Light",        "Palette.Light",        ThemeVariant.Light, "Theme_Name_Light"),
        new("Colourblind",  "Palette.Colourblind",  ThemeVariant.Dark,  "Theme_Name_Colourblind"),
        new("HighContrast", "Palette.HighContrast", ThemeVariant.Dark,  "Theme_Name_HighContrast"),
    ];

    // Keys unique to each dictionary, used to locate the live entries to replace without
    // relying on merge order.
    private const string PaletteSentinel = "Palette.Neutral.80";
    private const string TokensSentinel  = "Brush.Surface.Window";

    public IReadOnlyList<ThemeOption> Available { get; } =
        Catalog.Select(e => new ThemeOption(e.Id, e.DisplayNameKey)).ToList();

    /// <summary>
    /// Maps the OS-reported colour preferences to a catalog id. High-contrast wins outright
    /// regardless of the reported light/dark variant — the <c>HighContrast</c> palette is
    /// itself authored against <see cref="ThemeVariant.Dark"/> and isn't variant-aware.
    /// <c>null</c> (no platform settings available) falls back to <c>"Dark"</c>, matching
    /// the historical hardcoded default.
    /// </summary>
    internal static string DetectOsThemeId(PlatformColorValues? values)
    {
        if (values is null) return "Dark";
        if (values.ContrastPreference == ColorContrastPreference.High) return "HighContrast";
        return values.ThemeVariant == PlatformThemeVariant.Light ? "Light" : "Dark";
    }

    public void Apply(string id)
    {
        var entry = Catalog.FirstOrDefault(e => e.Id == id) ?? Catalog[0];
        var app   = Application.Current
            ?? throw new InvalidOperationException("No Application is running to apply a theme to.");

        var dicts = app.Resources.MergedDictionaries;

        // Drop whatever currently provides the palette/token keys — either the two separate
        // ResourceIncludes from App.axaml (first call) or a wrapper from a previous swap.
        for (var i = dicts.Count - 1; i >= 0; i--)
            if (dicts[i].TryGetResource(PaletteSentinel, null, out _) ||
                dicts[i].TryGetResource(TokensSentinel, null, out _))
                dicts.RemoveAt(i);

        // Palette FIRST, then Tokens, as siblings in one wrapper so Tokens' StaticResource
        // refs resolve against the new palette. Inserted at the front so the rest of the
        // merged dictionaries (Nodify, strings) keep their relative order.
        var wrapper = new ResourceDictionary
        {
            MergedDictionaries =
            {
                Include(entry.PaletteFile),
                Include("Tokens"),
            },
        };
        dicts.Insert(0, wrapper);

        app.RequestedThemeVariant = entry.Variant;
        ThemeService.Current.Bump();
    }

    private static ResourceInclude Include(string file) =>
        new((Uri?)null) { Source = new Uri(ResDir + file + ".axaml") };
}
