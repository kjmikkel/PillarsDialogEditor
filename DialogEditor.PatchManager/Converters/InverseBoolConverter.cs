using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.PatchManager.Converters;

public sealed class InverseBoolConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false || value is null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is false || value is null;
}
