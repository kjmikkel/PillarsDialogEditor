using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;

namespace DialogEditor.Avalonia.Controls;

/// <summary>
/// Draws a 2.5px coloured ring over the adorned control using Brush.Border.Focus
/// (visible in all four themes). Added to/removed from AdornerLayer by
/// MainWindow.axaml.cs as the guided tour advances.
/// </summary>
public sealed class TourHighlightAdorner : Control
{
    public override void Render(DrawingContext context)
    {
        // Resolve the focus-ring colour from the semantic token registry so the ring
        // matches the keyboard-focus indicator across all four themes. TokenBrushes.Resolve
        // is the one sanctioned seam — no hex literals or SolidColorBrush construction here.
        var brush = TokenBrushes.Resolve("Brush.Border.Focus");
        var pen   = new Pen(brush, 2.5);
        var rect  = new Rect(1.25, 1.25, Bounds.Width - 2.5, Bounds.Height - 2.5);
        context.DrawRectangle(null, pen, rect, 3, 3);
    }
}
