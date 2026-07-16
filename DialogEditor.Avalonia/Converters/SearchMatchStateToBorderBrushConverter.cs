using System;
using System.Globalization;
using Avalonia.Media;
using Avalonia.Data.Converters;
using DialogEditor.Avalonia.Theming;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Converters;

/// Match → an emphasis brush drawn as a node border (Severity.Info blue — distinct from the
/// diff-status and connect-mode borders); otherwise Transparent. Resolves a Brush.* token
/// (no hex — NoStrayHexTests).
public sealed class SearchMatchStateToBorderBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SearchMatchState.Match
            ? TokenBrushes.Resolve("Brush.Severity.Info")
            : (IBrush)Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
