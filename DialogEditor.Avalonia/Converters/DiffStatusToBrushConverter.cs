using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Converts a <see cref="DiffStatus"/> value to a background tint brush so that
/// added, removed, and changed nodes are visually distinguished on the canvas.
/// </summary>
public sealed class DiffStatusToBrushConverter : IValueConverter
{
    private static readonly ISolidColorBrush AddedBrush   = new SolidColorBrush(Color.Parse("#3a7a3a"));
    private static readonly ISolidColorBrush ChangedBrush = new SolidColorBrush(Color.Parse("#c08a2a"));
    private static readonly ISolidColorBrush RemovedBrush = new SolidColorBrush(Color.Parse("#7a2a2a"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DiffStatus status
            ? status switch
            {
                DiffStatus.Added   => AddedBrush,
                DiffStatus.Changed => ChangedBrush,
                DiffStatus.Removed => RemovedBrush,
                _                  => Brushes.Transparent
            }
            : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
