using DialogEditor.Core.Models;

namespace DialogEditor.ViewModels;

/// Where a link's position number sits on the canvas (issue 359). Every link of a node starts at the
/// same output port, so a fixed offset from the port would stack all the numbers; instead each label
/// takes a point a short way along its own curve, and links to different targets diverge there.
/// The curve is the one Nodify's Connection draws (NodifyAvalonia 6.6.0, Connection.GetBezierControlPoints,
/// with the Spacing 20 and static end offset 14 the canvas theme gives it, both ends horizontal, forward).
public static class LinkLabelGeometry
{
    private const double Spacing = 20;
    private const double EndOffset = 14;
    /// How far along the curve the label sits.
    private const double T = 0.3;

    /// Top-left of the label relative to the curve point: a little left and well above, so the digit
    /// sits beside the line rather than on it.
    public static readonly LayoutPoint LabelOffset = new(-4, -18);

    public static LayoutPoint LabelPoint(LayoutPoint source, LayoutPoint target)
    {
        // Nodify draws from the offset ends, then a straight Spacing stub, then the bezier.
        double sx = source.X + EndOffset, sy = source.Y;
        double tx = target.X - EndOffset, ty = target.Y;
        double dx = Math.Abs(tx - sx), dy = Math.Abs(ty - sy);
        double reach = Math.Min(Math.Max(Math.Min(100, dy), dx / 2), 100 + Math.Sqrt(dx * 25));

        double p0x = sx + Spacing,        p0y = sy;
        double p1x = p0x + reach,         p1y = p0y;
        double p3x = tx - Spacing,        p3y = ty;
        double p2x = p3x - reach,         p2y = p3y;

        double u = 1 - T;
        double c0 = u * u * u, c1 = 3 * T * u * u, c2 = 3 * T * T * u, c3 = T * T * T;
        return new LayoutPoint(
            c0 * p0x + c1 * p1x + c2 * p2x + c3 * p3x + LabelOffset.X,
            c0 * p0y + c1 * p1y + c2 * p2y + c3 * p3y + LabelOffset.Y);
    }
}
