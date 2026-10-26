using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DialogEditor.Avalonia.Controls;

/// Pure geometry for the spelling squiggle (issue 9): a zig-zag polyline that alternates
/// between <c>bottom - amplitude</c> and <c>bottom</c> every half period. Kept free of
/// drawing so it is unit-testable.
public static class SquiggleGeometry
{
    public static IReadOnlyList<Point> Wave(double left, double right, double bottom,
                                            double amplitude, double period)
    {
        if (right <= left || period <= 0) return [];

        var step = period / 2;
        var pts = new List<Point>();
        var low = true;                       // starts on the baseline (bottom)
        for (var x = left; x < right - 1e-6; x += step, low = !low)
            pts.Add(new Point(x, low ? bottom : bottom - amplitude));
        pts.Add(new Point(right, low ? bottom : bottom - amplitude));   // clamp to the word's end
        return pts;
    }
}

/// Non-hit-testable overlay that paints the squiggles. Rects are in the adorned
/// TextBox's coordinates, which is the adorner's own space.
internal sealed class SquiggleAdorner : Control
{
    public IReadOnlyList<Rect> Rects { get; set; } = [];

    public override void Render(DrawingContext context)
    {
        if (Rects.Count == 0) return;
        // Resource lookup (not a cached brush) so theme switches apply; Red only if the token is missing.
        IBrush brush = Brushes.Red;
        if (this.TryFindResource("Brush.Severity.Error", ActualThemeVariant, out var res) && res is IBrush b)
            brush = b;
        var pen = new Pen(brush, 1);

        foreach (var r in Rects)
        {
            var pts = SquiggleGeometry.Wave(r.Left, r.Right, r.Bottom + 1, 1.5, 4);
            if (pts.Count < 2) continue;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
            {
                g.BeginFigure(pts[0], false);
                for (var i = 1; i < pts.Count; i++) g.LineTo(pts[i]);
                g.EndFigure(false);
            }
            context.DrawGeometry(null, pen, geo);
        }
    }
}
