using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

/// <summary>Returns true when value is an integer greater than zero. Bind to
/// IsVisible. Pass ConverterParameter="invert" to return true when the count is
/// zero (for empty-state placeholders).</summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hasItems = value is int n && n > 0;
        var invert   = parameter is string s
            && s.Equals("invert", StringComparison.OrdinalIgnoreCase);
        return invert ? !hasItems : hasItems;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
