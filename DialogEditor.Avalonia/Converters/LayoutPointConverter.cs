using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Converts between LayoutPoint (ViewModel) and Avalonia.Point (Nodify bindings).
/// WPF port: swap Avalonia.Point for System.Windows.Point.
/// </summary>
public sealed class LayoutPointConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is LayoutPoint p ? new Point(p.X, p.Y) : AvaloniaProperty.UnsetValue;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Point p ? new LayoutPoint(p.X, p.Y) : default(LayoutPoint);
}
