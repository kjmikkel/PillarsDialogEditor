using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// IMultiValueConverter — values[0] = SpeakerCategory, values[1] = DisplayType string.
/// ConverterParameter: "body" / "footer" / omit = header. Returns Brush.Node.Bark.*
/// when DisplayType is "Bark"; otherwise reuses SpeakerCategoryToBrushConverter.Key so
/// the conversation palette is defined exactly once (the old duplicate RGB table is gone).
/// </summary>
public sealed class NodeColorConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var cat         = values.Count > 0 && values[0] is SpeakerCategory c ? c : SpeakerCategory.Npc;
        var displayType = values.Count > 1 ? values[1] as string ?? string.Empty : string.Empty;
        var zone        = parameter as string;

        if (displayType == "Bark")
        {
            var part = zone switch { "body" => "Body", "footer" => "Footer", _ => "Header" };
            return TokenBrushes.Resolve($"Brush.Node.Bark.{part}");
        }
        return TokenBrushes.Resolve(SpeakerCategoryToBrushConverter.Key(cat, zone));
    }
}
