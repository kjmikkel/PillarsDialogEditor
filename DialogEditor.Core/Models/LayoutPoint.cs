namespace DialogEditor.Core.Models;

/// <summary>
/// Platform-agnostic 2-D point used for canvas node positions.
/// Converted to/from Avalonia.Point via LayoutPointConverter.
/// </summary>
public readonly record struct LayoutPoint(double X, double Y)
{
    public static readonly LayoutPoint Zero = new(0, 0);
}
