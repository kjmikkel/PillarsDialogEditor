using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

/// <summary>Formats a 0..n double as a percentage string ("100%", "125%", "85%"),
/// so a picker's option list carries no hardcoded label per option. Serves both the
/// Settings font-scale multiplier (1.0, 1.25, ...) and the near-duplicate similarity
/// threshold (0.75, 0.85, ...) — hence the neutral name.</summary>
public sealed class DoubleToPercentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double scale ? $"{scale * 100:0}%" : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
