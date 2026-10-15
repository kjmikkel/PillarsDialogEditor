namespace DialogEditor.Core.Layout;

/// <summary>
/// Issue 82: the conversation-canvas card's dimensions, in canvas units (px at 100% zoom and
/// font scale 1.0). This is the single source of truth: Tokens.axaml declares the same values
/// as Canvas.* tokens for the node template, and AutoLayoutService places cards using
/// <see cref="EstimateHeight"/>. CanvasCardRenderTests renders the real template and fails if
/// the two drift apart. Font scale never changes these numbers: it magnifies the whole canvas
/// instead (ConversationView), so layouts and line breaks are the same at every scale.
/// See docs/superpowers/specs/2026-10-15-canvas-readable-cards-design.md.
/// </summary>
public static class CardMetrics
{
    public const double Width            = 240;
    public const double BorderThickness  = 1;
    public const double HeaderFontSize   = 12;
    public const double BodyFontSize     = 12;
    public const double FooterFontSize   = 10;
    public const double HeaderLineHeight = 16;
    public const double BodyLineHeight   = 16;
    public const double FooterLineHeight = 13;
    public const int    MaxBodyLines     = 4;
    public const double ColumnGap        = 100;
    public const double RowGap           = 28;

    private const double HeaderPaddingY = 3, BodyPaddingY = 4, FooterPaddingY = 2, PaddingX = 6;

    // Deliberately wider than the body font's real average glyph: an estimate that is too tall
    // only adds space, one that is too short overlaps the next card. CanvasCardRenderTests
    // checks that it never under-counts the lines the template actually renders.
    private const double AverageCharWidth = 6.4;

    private const double BodyTextWidth = Width - 2 * BorderThickness - 2 * PaddingX;

    public static double HeaderHeight => HeaderLineHeight + 2 * HeaderPaddingY;
    public static double FooterHeight => FooterLineHeight + 2 * FooterPaddingY;
    public static double MaxHeight    => HeightFor(MaxBodyLines);

    public static int EstimateBodyLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1;   // the #84 placeholder is one line
        var perLine = Math.Max(1, (int)Math.Floor(BodyTextWidth / AverageCharWidth));
        var lines = 0;
        foreach (var paragraph in text.Split('\n'))
            lines += Math.Max(1, (int)Math.Ceiling(paragraph.TrimEnd('\r').Length / (double)perLine));
        return Math.Clamp(lines, 1, MaxBodyLines);
    }

    public static double EstimateHeight(string? text) => HeightFor(EstimateBodyLines(text));

    private static double HeightFor(int bodyLines) =>
        2 * BorderThickness + HeaderHeight + bodyLines * BodyLineHeight + 2 * BodyPaddingY + FooterHeight;
}
