namespace DialogEditor.Core.Layout;

/// <summary>
/// GitHub issue 221: how much of a canvas card shows at a given canvas zoom. Zoomed out, a card
/// is its header and one line with no footer, so a large hub reads as a shape rather than a wall
/// of tiny boxes. Node positions never change with zoom: layout keeps placing cards by their
/// normal height (<see cref="CardMetrics.EstimateHeight"/>), and a compact card is never taller
/// than that, so it only leaves space. The zoom is Nodify's ViewportZoom; font scale magnifies
/// the canvas outside it, so the bands mean the same at every text size.
/// </summary>
public static class CardDetail
{
    /// Below this zoom cards are compact.
    public const double CompactBelowZoom = 0.75;

    public static bool IsCompactAt(double zoom) => zoom < CompactBelowZoom;

    public static int BodyLinesAt(double zoom) => IsCompactAt(zoom) ? 1 : CardMetrics.MaxBodyLines;

    public static bool ShowsFooterAt(double zoom) => !IsCompactAt(zoom);
}
