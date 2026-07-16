using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Converters;

/// Dimmed → faded; None/Match → full opacity. Mirrors the previous BoolToOpacity dim but
/// keyed off the unified SearchMatchState.
public sealed class SearchMatchStateToOpacityConverter : IValueConverter
{
    private const double DimmedOpacity = 0.35;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SearchMatchState.Dimmed ? DimmedOpacity : 1.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
