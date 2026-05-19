using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace DialogEditor.Avalonia.Converters;

/// Returns a green tint for new (not-yet-on-disk) conversations, normal text colour otherwise.
public sealed class BoolToNewConversationBrushConverter : IValueConverter
{
    private static readonly ISolidColorBrush NewBrush    = new SolidColorBrush(Color.FromRgb(0x7d, 0xce, 0xa0));
    private static readonly ISolidColorBrush NormalBrush = new SolidColorBrush(Color.FromRgb(0xcc, 0xcc, 0xcc));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? NewBrush : NormalBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
