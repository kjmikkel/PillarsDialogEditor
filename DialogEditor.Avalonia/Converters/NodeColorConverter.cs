using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// IMultiValueConverter — values[0] = SpeakerCategory, values[1] = DisplayType string.
/// ConverterParameter: "body" = card body, "footer" = card footer, omit = header.
/// Returns amber tones when DisplayType is "Bark"; speaker-category tones otherwise.
/// </summary>
public sealed class NodeColorConverter : IMultiValueConverter
{
    // Bark palette
    private static readonly ISolidColorBrush BarkHeader = new SolidColorBrush(Color.FromRgb(0x7A, 0x5C, 0x00));
    private static readonly ISolidColorBrush BarkBody   = new SolidColorBrush(Color.FromRgb(0xFF, 0xF8, 0xDC));
    private static readonly ISolidColorBrush BarkFooter = new SolidColorBrush(Color.FromRgb(0xE8, 0xD0, 0x80));

    // Conversation palette — mirrors SpeakerCategoryToBrushConverter
    private static readonly ISolidColorBrush NpcHeader      = new SolidColorBrush(Color.FromRgb(0x7b, 0x24, 0x1c));
    private static readonly ISolidColorBrush PlayerHeader   = new SolidColorBrush(Color.FromRgb(0x1a, 0x52, 0x76));
    private static readonly ISolidColorBrush NarratorHeader = new SolidColorBrush(Color.FromRgb(0x0e, 0x66, 0x55));
    private static readonly ISolidColorBrush ScriptHeader   = new SolidColorBrush(Color.FromRgb(0x2c, 0x3e, 0x50));

    private static readonly ISolidColorBrush NpcBody      = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xD0));
    private static readonly ISolidColorBrush PlayerBody   = new SolidColorBrush(Color.FromRgb(0xD5, 0xE8, 0xF5));
    private static readonly ISolidColorBrush NarratorBody = new SolidColorBrush(Color.FromRgb(0xD5, 0xF0, 0xE8));
    private static readonly ISolidColorBrush ScriptBody   = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    private static readonly ISolidColorBrush NpcFooter      = new SolidColorBrush(Color.FromRgb(0xE8, 0xE0, 0xB0));
    private static readonly ISolidColorBrush PlayerFooter   = new SolidColorBrush(Color.FromRgb(0xB0, 0xCD, 0xE8));
    private static readonly ISolidColorBrush NarratorFooter = new SolidColorBrush(Color.FromRgb(0xB0, 0xE0, 0xD5));
    private static readonly ISolidColorBrush ScriptFooter   = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));

    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var cat         = values.Count > 0 && values[0] is SpeakerCategory c ? c : SpeakerCategory.Npc;
        var displayType = values.Count > 1 ? values[1] as string ?? string.Empty : string.Empty;
        var zone        = parameter as string;

        if (displayType == "Bark")
        {
            return zone switch
            {
                "body"   => BarkBody,
                "footer" => BarkFooter,
                _        => BarkHeader
            };
        }

        return zone switch
        {
            "body" => cat switch
            {
                SpeakerCategory.Player   => PlayerBody,
                SpeakerCategory.Narrator => NarratorBody,
                SpeakerCategory.Script   => ScriptBody,
                _                        => NpcBody
            },
            "footer" => cat switch
            {
                SpeakerCategory.Player   => PlayerFooter,
                SpeakerCategory.Narrator => NarratorFooter,
                SpeakerCategory.Script   => ScriptFooter,
                _                        => NpcFooter
            },
            _ => cat switch
            {
                SpeakerCategory.Player   => PlayerHeader,
                SpeakerCategory.Narrator => NarratorHeader,
                SpeakerCategory.Script   => ScriptHeader,
                _                        => NpcHeader
            }
        };
    }
}
