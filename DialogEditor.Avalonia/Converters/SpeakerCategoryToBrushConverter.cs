using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Converts SpeakerCategory → node brush by resolving Brush.Node.* tokens.
/// ConverterParameter: "body" / "footer" / omitted = header. The palette lives in
/// Tokens.axaml — this converter holds no colours (was a hand-copied RGB table that
/// drifted against NodeColorConverter; both now share the same keys).
/// </summary>
public sealed class SpeakerCategoryToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var cat = value is SpeakerCategory c ? c : SpeakerCategory.Npc;
        return TokenBrushes.Resolve(Key(cat, parameter as string));
    }

    internal static string Key(SpeakerCategory cat, string? zone)
    {
        var subject = cat switch
        {
            SpeakerCategory.Player   => "Player",
            SpeakerCategory.Narrator => "Narrator",
            SpeakerCategory.Script   => "Script",
            _                        => "Npc",
        };
        var part = zone switch { "body" => "Body", "footer" => "Footer", _ => "Header" };
        return $"Brush.Node.{subject}.{part}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
