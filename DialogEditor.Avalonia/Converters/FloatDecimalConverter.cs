using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

/// Bridges float (ConnectionViewModel.RandomWeight) ↔ decimal? (NumericUpDown.Value).
public sealed class FloatDecimalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is float f ? (decimal?)( decimal)f : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? (float)d : (object?)null;
}
