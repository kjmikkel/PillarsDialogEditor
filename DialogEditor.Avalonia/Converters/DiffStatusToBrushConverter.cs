using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Converts a <see cref="DiffStatus"/> value to a background tint brush so that
/// added, removed, and changed nodes are visually distinguished on the canvas.
/// Resolves Brush.Diff.*.Fill tokens; Unchanged/null have no fill token and stay
/// transparent. See docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md §7.2.
/// </summary>
public sealed class DiffStatusToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DiffStatus status
            ? status switch
            {
                DiffStatus.Added   => TokenBrushes.Resolve("Brush.Diff.Added.Fill"),
                DiffStatus.Changed => TokenBrushes.Resolve("Brush.Diff.Changed.Fill"),
                DiffStatus.Removed => TokenBrushes.Resolve("Brush.Diff.Removed.Fill"),
                _                  => Brushes.Transparent,
            }
            : Brushes.Transparent;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
