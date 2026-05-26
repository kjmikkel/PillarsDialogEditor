using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Core.Analytics;

namespace DialogEditor.Avalonia.Converters;

public sealed class FlowIssueKindToSeverityBrushConverter : IValueConverter
{
    private static readonly ISolidColorBrush Red   = new SolidColorBrush(Color.FromRgb(0xc0, 0x39, 0x2b));
    private static readonly ISolidColorBrush Amber = new SolidColorBrush(Color.FromRgb(0xb8, 0x76, 0x0a));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is FlowIssueKind.Unreachable ? Red : Amber;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
