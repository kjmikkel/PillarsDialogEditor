using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Converters;

/// Renders the empty QTD string as a localised "(default)" label.
/// ConverterParameter should be the localised display string for "". When the view
/// omits it we fall back to the same resource it would have passed, never to English --
/// the fallback used to be a hard-coded "(default)".
public sealed class QTDDisplayConverter : IValueConverter
{
    private static string Default => Loc.Get("Option_QTD_Default");

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s.Length == 0)
            return parameter as string ?? Default;
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s == (parameter as string ?? Default))
            return "";
        return value;
    }
}
