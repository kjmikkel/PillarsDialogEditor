using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

public class EnumToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value?.ToString() == parameter as string;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string s)
            return Enum.Parse(targetType, s);
        return BindingOperations.DoNothing;
    }
}
