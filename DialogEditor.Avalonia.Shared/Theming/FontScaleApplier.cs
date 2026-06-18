using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Applies a font-size scale factor to the live FontSize.* resource tokens at runtime.
/// Because the tokens are referenced via <c>{DynamicResource FontSize.*}</c>, every bound
/// control re-evaluates immediately after <see cref="Apply"/> writes the new values. Bumps
/// <see cref="FontScaleService.Current"/> so canvas MultiBinding converters that can't use
/// DynamicResource can force a re-evaluate via their ThemeService/FontScaleService subscription.
/// See design spec docs/superpowers/specs/2026-06-14-fontsize-scale-setting-design.md.
/// </summary>
public sealed class FontScaleApplier : IFontScaleApplier
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

        FontScaleService.Current.Bump();
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
