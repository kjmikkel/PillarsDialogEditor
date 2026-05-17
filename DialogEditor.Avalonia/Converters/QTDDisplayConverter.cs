using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

/// Renders the empty QTD string as a localised "(default)" label.
/// ConverterParameter should be the localised display string for "".
public sealed class QTDDisplayConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s.Length == 0)
            return parameter as string ?? "(default)";
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s == (parameter as string ?? "(default)"))
            return "";
        return value;
    }
}
