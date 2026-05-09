using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Converts between LayoutPoint (ViewModel) and Avalonia.Point (Nodify bindings).
/// </summary>
public sealed class LayoutPointConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LayoutPoint p ? new Point(p.X, p.Y) : AvaloniaProperty.UnsetValue;

    // OneWayToSource: Nodify writes NodeInput/NodeOutput.Anchor (Avalonia.Point) back to
    // ConnectorViewModel.Anchor (LayoutPoint) so Connection endpoints draw from the correct ports.
    // Guard against AvaloniaProperty.UnsetValue which is not a Point.
    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || value == AvaloniaProperty.UnsetValue) return default(LayoutPoint);
        return value is Point p ? new LayoutPoint(p.X, p.Y) : default(LayoutPoint);
    }
}
