namespace DialogEditor.Core.Models;

/// <summary>
/// Platform-agnostic 2-D point used for canvas node positions.
/// WPF: converted to/from System.Windows.Point via LayoutPointConverter.
/// Avalonia: converted to/from Avalonia.Point via the same converter pattern.
/// </summary>
public readonly record struct LayoutPoint(double X, double Y)
{
    public static readonly LayoutPoint Zero = new(0, 0);
}
