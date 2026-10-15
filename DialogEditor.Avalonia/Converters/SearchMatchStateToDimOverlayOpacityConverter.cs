using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Converters;

/// Opacity of the card's dimming overlay: Dimmed → 0.6 (canvas-coloured veil), None/Match → 0.
/// Issue 82: replaces fading the whole card (Opacity 0.35), which made dimmed cards translucent so
/// connection lines showed through their text. The card itself now always stays opaque.
public sealed class SearchMatchStateToDimOverlayOpacityConverter : IValueConverter
{
    private const double DimmedOpacity = 0.6;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SearchMatchState.Dimmed ? DimmedOpacity : 0.0;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
