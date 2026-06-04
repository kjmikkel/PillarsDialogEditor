using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

/// Formats a commit's DateTimeOffset as the culture's short date (the OS regional
/// format, since the app never overrides CurrentCulture). The full ISO timestamp
/// is shown separately as the row tooltip.
public sealed class CommitDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset d ? d.ToString("d", culture) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
