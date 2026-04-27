using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

/// <summary>Returns true when value is an integer greater than zero. Bind to IsVisible.</summary>
public sealed class CountToBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int n && n > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
