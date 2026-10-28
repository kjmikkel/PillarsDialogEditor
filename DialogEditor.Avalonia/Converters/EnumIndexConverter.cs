using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

/// Maps an enum to its numeric value and back, so a ComboBox whose items are listed in the
/// enum's declaration order (with localised labels) can bind SelectedIndex to the enum.
public sealed class EnumIndexConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Enum e ? System.Convert.ToInt32(e, CultureInfo.InvariantCulture) : BindingOperations.DoNothing;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int i && i >= 0 && targetType.IsEnum
            ? Enum.ToObject(targetType, i)
            : BindingOperations.DoNothing;
}
