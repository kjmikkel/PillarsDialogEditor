using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace DialogEditor.Avalonia.Theming;

/// <summary>
/// Resolves a semantic Brush.* token from the application resource registry
/// (Tokens.axaml). Converters call this instead of constructing brushes, so colour
/// has exactly one source of truth and the duplicated-RGB drift bug is impossible.
/// Fails fast on an unknown key — the keys are compile-time constants and
/// TokenRegistryTests guarantees every declared token resolves. See
/// docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md §10.
/// </summary>
public static class TokenBrushes
{
    public static IBrush Resolve(string key)
    {
        var app = Application.Current
            ?? throw new KeyNotFoundException($"No Application to resolve brush token '{key}'.");
        if (app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush)
            return brush;
        throw new KeyNotFoundException($"Brush token '{key}' is not defined in the registry.");
    }
}
