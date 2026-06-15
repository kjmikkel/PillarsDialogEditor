using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

/// <summary>Formats a FontScale multiplier (1.0, 1.25, ...) as a percentage string
/// ("100%", "125%") for the Settings font-scale picker, avoiding a hardcoded label per
/// option.</summary>
public sealed class FontScaleToPercentConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is double scale ? $"{scale * 100:0}%" : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
