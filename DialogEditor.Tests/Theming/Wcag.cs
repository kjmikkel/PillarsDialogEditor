using Avalonia.Media;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// WCAG 2.x relative-luminance contrast ratio. Used to enforce the Layer 1 accessibility
/// targets at the colour-value level (spec §5). Alpha is ignored — the curated contrast
/// pairs are all opaque tokens.
/// </summary>
internal static class Wcag
{
    public static double ContrastRatio(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(Color c)
        => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(byte v)
    {
        var s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
