using System.Globalization;
using System.Windows;
using System.Windows.Data;
using DialogEditor.Core.Models;

namespace DialogEditor.WPF.Converters;

/// <summary>
/// Converts between the platform-agnostic LayoutPoint (ViewModel) and
/// System.Windows.Point (Nodify ItemContainer.Location / Minimap item location).
/// Avalonia port: replace the target type with Avalonia.Point.
/// </summary>
[ValueConversion(typeof(LayoutPoint), typeof(Point))]
public sealed class LayoutPointConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is LayoutPoint p ? new Point(p.X, p.Y) : DependencyProperty.UnsetValue;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Point p ? new LayoutPoint(p.X, p.Y) : default(LayoutPoint);
}
