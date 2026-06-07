using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.Avalonia.Theming;

namespace DialogEditor.Avalonia.Converters;

/// Active vs dim text for the female-VO indicator. Resolves
/// Brush.Text.Female.Active / Brush.Text.Female.Dim (spec §7.5).
public sealed class BoolToFemaleTextBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? TokenBrushes.Resolve("Brush.Text.Female.Active")
            : TokenBrushes.Resolve("Brush.Text.Female.Dim");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
