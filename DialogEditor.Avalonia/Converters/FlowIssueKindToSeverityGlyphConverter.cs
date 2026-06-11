using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.Core.Analytics;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Maps a <see cref="FlowIssueKind"/> to a severity glyph, alongside
/// FlowIssueKindToSeverityBrushConverter's colour: Unreachable = ⛔ (error),
/// everything else = ⚠ (warning). See
/// docs/superpowers/specs/2026-06-11-layer2.5-non-colour-encoding-design.md §5.
/// </summary>
public sealed class FlowIssueKindToSeverityGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is FlowIssueKind kind && kind == FlowIssueKind.Unreachable
            ? "⛔"  // ⛔
            : "⚠"; // ⚠

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
