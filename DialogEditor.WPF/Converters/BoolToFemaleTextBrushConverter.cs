using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace DialogEditor.WPF.Converters;

[ValueConversion(typeof(bool), typeof(Brush))]
public class BoolToFemaleTextBrushConverter : IValueConverter
{
    private static readonly Brush ActiveBrush = new SolidColorBrush(Color.FromRgb(0xe8, 0xe8, 0xe8));
    private static readonly Brush DimBrush    = new SolidColorBrush(Color.FromRgb(0x55, 0x55, 0x55));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? ActiveBrush : DimBrush;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
