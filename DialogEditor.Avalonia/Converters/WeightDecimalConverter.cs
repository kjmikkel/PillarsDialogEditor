using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// Bridges int (ConnectionViewModel.RandomWeight) ↔ decimal? (NumericUpDown.Value). The field
/// steps by 1, but a fraction can still be typed by hand; it is rounded to the whole weight
/// the games store (issue 129).
public sealed class WeightDecimalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i ? (decimal?)i : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? LinkWeight.FromInput(d) : (object?)null;
}
