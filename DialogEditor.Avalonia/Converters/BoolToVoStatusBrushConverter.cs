using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.Avalonia.Theming;

namespace DialogEditor.Avalonia.Converters;

/// Maps a VoStatusIsFound bool to a coloured brush.
/// true  → Brush.Text.Status.Success (green, VO found)
/// false → Brush.Severity.Error      (red, VO missing)
public sealed class BoolToVoStatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? TokenBrushes.Resolve("Brush.Text.Status.Success")
            : TokenBrushes.Resolve("Brush.Severity.Error");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
