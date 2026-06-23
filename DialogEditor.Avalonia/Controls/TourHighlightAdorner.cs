using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;

namespace DialogEditor.Avalonia.Controls;

/// <summary>
/// Draws a double-line accent ring over the adorned control: a 4px outer ring and a
/// 2px inner ring, both using Brush.Border.Focus. Added to/removed from AdornerLayer
/// by MainWindow.axaml.cs as the guided tour advances.
/// </summary>
public sealed class TourHighlightAdorner : Control
{
    public override void Render(DrawingContext context)
    {
        // TokenBrushes.Resolve is the one sanctioned seam — no hex literals or
        // SolidColorBrush construction permitted by NoStrayHexTests.
        var accent = TokenBrushes.Resolve("Brush.Border.Focus");
        context.DrawRectangle(null, new Pen(accent, 4),
            new Rect(2, 2, Bounds.Width - 4, Bounds.Height - 4), 4, 4);
        context.DrawRectangle(null, new Pen(accent, 2),
            new Rect(1, 1, Bounds.Width - 2, Bounds.Height - 2), 3, 3);
    }
}
