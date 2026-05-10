using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.ViewModels.Models;

namespace DialogEditor.Avalonia.Converters;

public sealed class PropertyValueStyleToBrushConverter : IValueConverter
{
    private static readonly ISolidColorBrush ConditionBrush = new SolidColorBrush(Color.FromRgb(0xe8, 0xa0, 0x20));
    private static readonly ISolidColorBrush ScriptBrush    = new SolidColorBrush(Color.FromRgb(0x7d, 0xce, 0xa0));
    private static readonly ISolidColorBrush CodeBrush      = new SolidColorBrush(Color.FromRgb(0x9c, 0xdc, 0xfe));
    private static readonly ISolidColorBrush DefaultBrush   = new SolidColorBrush(Color.FromRgb(0xe8, 0xe8, 0xe8));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is PropertyValueStyle style ? style switch
        {
            PropertyValueStyle.Condition => ConditionBrush,
            PropertyValueStyle.Script    => ScriptBrush,
            PropertyValueStyle.Code      => CodeBrush,
            _                            => DefaultBrush,
        } : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
