using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Applies a font-size scale factor to the live FontSize.* tokens, once at startup
/// (after ThemeApplier.Apply, which reloads Tokens.axaml and would otherwise reset any
/// earlier scaling). Every FontSize.* StaticResource binding resolves against the
/// mutated dictionary for windows constructed afterwards — see design spec
/// docs/superpowers/specs/2026-06-14-fontsize-scale-setting-design.md.
/// </summary>
public sealed class FontScaleApplier
{
    private const string FontSizeSentinel = "FontSize.Body";

    public void Apply(double scale)
    {
        var app = Application.Current
            ?? throw new InvalidOperationException("No Application is running to apply a font scale to.");

        var dict = FindDictionary(app.Resources, FontSizeSentinel)
            ?? throw new InvalidOperationException($"No resource dictionary defines '{FontSizeSentinel}'.");

        foreach (var (key, baseValue) in FontSizeTokens.BaseValues)
            dict[key] = baseValue * scale;
    }

    private static IResourceDictionary? FindDictionary(IResourceProvider provider, string key)
    {
        var dict = provider as IResourceDictionary
            ?? (provider as ResourceInclude)?.Loaded as IResourceDictionary;

        if (dict is null) return null;
        if (dict.ContainsKey(key)) return dict;

        foreach (var merged in dict.MergedDictionaries)
            if (FindDictionary(merged, key) is { } found)
                return found;

        return null;
    }
}
