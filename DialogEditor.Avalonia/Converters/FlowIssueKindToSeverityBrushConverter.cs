using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.Avalonia.Theming;
using DialogEditor.Core.Analytics;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Maps a <see cref="FlowIssueKind"/> to a severity brush: Unreachable = error,
/// everything else = warning. Resolves Brush.Severity.* tokens (spec §7.3).
/// </summary>
public sealed class FlowIssueKindToSeverityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is FlowIssueKind kind && kind == FlowIssueKind.Unreachable
            ? TokenBrushes.Resolve("Brush.Severity.Error")
            : TokenBrushes.Resolve("Brush.Severity.Warning");

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
