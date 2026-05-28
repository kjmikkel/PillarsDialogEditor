using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

public class StringEqualsBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is string s && parameter is string p &&
           s.Equals(p, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? parameter : BindingOperations.DoNothing;
}
