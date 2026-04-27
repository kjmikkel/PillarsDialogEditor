using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DialogEditor.Avalonia.Converters;

public sealed class BoolToFemaleTextBrushConverter : IValueConverter
{
    private static readonly ISolidColorBrush ActiveBrush = new SolidColorBrush(Color.FromRgb(0xe8, 0xe8, 0xe8));
    private static readonly ISolidColorBrush DimBrush    = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? ActiveBrush : DimBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
