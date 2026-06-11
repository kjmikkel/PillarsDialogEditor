using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// IMultiValueConverter — values[0] = SpeakerCategory, values[1] = DisplayType string.
/// Returns a small (0–9 viewbox) Geometry shape that encodes node type independently of
/// colour: Npc = circle, Player = square, Narrator = triangle, Script = diamond,
/// Bark = star (DisplayType == "Bark" overrides SpeakerCategory, mirroring
/// NodeColorConverter). See docs/superpowers/specs/2026-06-11-layer2.5-non-colour-encoding-design.md §2.
/// </summary>
public sealed class NodeTypeShapeConverter : IMultiValueConverter
{
    private static readonly Geometry CircleGeometry =
        Geometry.Parse("M4.5,0 A4.5,4.5 0 1 1 4.5,9 A4.5,4.5 0 1 1 4.5,0 Z");
    private static readonly Geometry SquareGeometry =
        Geometry.Parse("M0,0 L9,0 L9,9 L0,9 Z");
    private static readonly Geometry TriangleGeometry =
        Geometry.Parse("M4.5,0 L9,9 L0,9 Z");
    private static readonly Geometry DiamondGeometry =
        Geometry.Parse("M4.5,0 L9,4.5 L4.5,9 L0,4.5 Z");
    private static readonly Geometry StarGeometry =
        Geometry.Parse("M4.5,0 L5.6,3.3 L9,3.3 L6.2,5.4 L7.3,8.7 L4.5,6.6 L1.7,8.7 L2.8,5.4 L0,3.3 L3.4,3.3 Z");

    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var cat         = values.Count > 0 && values[0] is SpeakerCategory c ? c : SpeakerCategory.Npc;
        var displayType = values.Count > 1 ? values[1] as string : null;

        if (displayType == "Bark") return StarGeometry;

        return cat switch
        {
            SpeakerCategory.Player   => SquareGeometry,
            SpeakerCategory.Narrator => TriangleGeometry,
            SpeakerCategory.Script   => DiamondGeometry,
            _                        => CircleGeometry,
        };
    }

    public object? ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
