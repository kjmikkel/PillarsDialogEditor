using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Resolves an annotation brush token from the colour key stored on the AnnotationViewModel.
/// Values[0] = ColorKey (string, e.g. "Yellow").
/// ConverterParameter = zone suffix: "Fill", "Header", or "Stroke".
/// Returns the <c>Brush.Annotation.{ColorKey}.{zone}</c> token via TokenBrushes.Resolve.
/// </summary>
public sealed class AnnotationColorConverter : IMultiValueConverter
{
    private static readonly HashSet<string> ValidKeys =
        ["Yellow", "Red", "Green", "Blue", "Purple", "Teal", "Orange"];

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var colorKey = values.Count > 0 ? values[0] as string ?? "Yellow" : "Yellow";
        if (!ValidKeys.Contains(colorKey)) colorKey = "Yellow";
        var zone = parameter as string ?? "Fill";
        return TokenBrushes.Resolve($"Brush.Annotation.{colorKey}.{zone}");
    }
}
