namespace DialogEditor.Core.Layout;

public static class CanvasMath
{
    /// Converts a screen-space point to canvas coordinates given viewport zoom and origin.
    public static (double X, double Y) ScreenToCanvas(
        double screenX, double screenY,
        double zoom,
        double originX, double originY)
        => (screenX / zoom + originX, screenY / zoom + originY);
}
