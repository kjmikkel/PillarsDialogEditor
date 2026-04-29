using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Converts SpeakerCategory → brush.
/// ConverterParameter: "body" = card body background, "footer" = card footer background,
/// omitted = node header background (default).
/// </summary>
public sealed class SpeakerCategoryToBrushConverter : IValueConverter
{
    // Header (dark)
    private static readonly ISolidColorBrush NpcHeader      = new SolidColorBrush(Color.FromRgb(0x7b, 0x24, 0x1c));
    private static readonly ISolidColorBrush PlayerHeader   = new SolidColorBrush(Color.FromRgb(0x1a, 0x52, 0x76));
    private static readonly ISolidColorBrush NarratorHeader = new SolidColorBrush(Color.FromRgb(0x0e, 0x66, 0x55));
    private static readonly ISolidColorBrush ScriptHeader   = new SolidColorBrush(Color.FromRgb(0x2c, 0x3e, 0x50));

    // Body (light)
    private static readonly ISolidColorBrush NpcBody      = new SolidColorBrush(Color.FromRgb(0xF5, 0xF0, 0xD0));
    private static readonly ISolidColorBrush PlayerBody   = new SolidColorBrush(Color.FromRgb(0xD5, 0xE8, 0xF5));
    private static readonly ISolidColorBrush NarratorBody = new SolidColorBrush(Color.FromRgb(0xD5, 0xF0, 0xE8));
    private static readonly ISolidColorBrush ScriptBody   = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xE0));

    // Footer (mid)
    private static readonly ISolidColorBrush NpcFooter      = new SolidColorBrush(Color.FromRgb(0xE8, 0xE0, 0xB0));
    private static readonly ISolidColorBrush PlayerFooter   = new SolidColorBrush(Color.FromRgb(0xB0, 0xCD, 0xE8));
    private static readonly ISolidColorBrush NarratorFooter = new SolidColorBrush(Color.FromRgb(0xB0, 0xE0, 0xD5));
    private static readonly ISolidColorBrush ScriptFooter   = new SolidColorBrush(Color.FromRgb(0xC8, 0xC8, 0xC8));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not SpeakerCategory cat) return NpcHeader;

        return parameter as string switch
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

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
