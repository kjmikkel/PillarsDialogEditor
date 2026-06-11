using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Converts a <see cref="DiffStatus"/> value to the corner-badge glyph shown alongside the
/// existing Brush.Diff.*.Fill border colour. Unchanged/null return an empty string, which
/// (paired with DiffStatusToBrushConverter's transparent fill) leaves the badge invisible.
/// See docs/superpowers/specs/2026-06-11-layer2.5-non-colour-encoding-design.md §3.
/// </summary>
public sealed class DiffStatusToGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DiffStatus status
            ? status switch
            {
                DiffStatus.Added   => "+",
                DiffStatus.Changed => "~",
                DiffStatus.Removed => "−",
                _                  => "",
            }
            : "";

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
