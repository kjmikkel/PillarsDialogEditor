using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DialogEditor.WPF.Converters;

[ValueConversion(typeof(bool), typeof(SolidColorBrush))]
public class BoolToHeaderBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush NpcBrush =
        new(Color.FromRgb(0x7b, 0x24, 0x1c));
    private static readonly SolidColorBrush PlayerBrush =
        new(Color.FromRgb(0x1a, 0x52, 0x76));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? PlayerBrush : NpcBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
