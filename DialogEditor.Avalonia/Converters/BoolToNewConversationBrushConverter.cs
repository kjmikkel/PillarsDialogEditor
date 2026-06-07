using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.Avalonia.Theming;

namespace DialogEditor.Avalonia.Converters;

/// Returns a green tint for new (not-yet-on-disk) conversations, normal text colour
/// otherwise. Resolves Brush.Text.Status.New / Brush.Text.Secondary (spec §7.5/§7.6).
public sealed class BoolToNewConversationBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true
            ? TokenBrushes.Resolve("Brush.Text.Status.New")
            : TokenBrushes.Resolve("Brush.Text.Secondary");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
