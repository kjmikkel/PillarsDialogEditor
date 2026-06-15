namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Canonical unscaled FontSize.* token values (Tokens.axaml). Single source of truth
/// shared by FontScaleApplier (computes scaled values) and FontSizeTokenTests (pins the
/// unscaled values) so the two cannot drift apart. See design spec
/// docs/superpowers/specs/2026-06-14-fontsize-scale-setting-design.md.
/// </summary>
public static class FontSizeTokens
{
    public static readonly IReadOnlyDictionary<string, double> BaseValues = new Dictionary<string, double>
    {
        ["FontSize.Caption"]  = 9,
        ["FontSize.Small"]    = 10,
        ["FontSize.Label"]    = 11,
        ["FontSize.Body"]     = 12,
        ["FontSize.Medium"]   = 13,
        ["FontSize.Subtitle"] = 14,
        ["FontSize.Title"]    = 18,
        ["FontSize.Display"]  = 32,
    };
}
