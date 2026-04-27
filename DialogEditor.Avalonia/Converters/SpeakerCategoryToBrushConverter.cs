using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

public sealed class SpeakerCategoryToBrushConverter : IValueConverter
{
    private static readonly ISolidColorBrush NpcBrush      = new SolidColorBrush(Color.FromRgb(0x7b, 0x24, 0x1c));
    private static readonly ISolidColorBrush PlayerBrush   = new SolidColorBrush(Color.FromRgb(0x1a, 0x52, 0x76));
    private static readonly ISolidColorBrush NarratorBrush = new SolidColorBrush(Color.FromRgb(0x0e, 0x66, 0x55));
    private static readonly ISolidColorBrush ScriptBrush   = new SolidColorBrush(Color.FromRgb(0x2c, 0x3e, 0x50));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is SpeakerCategory cat ? cat switch
        {
            SpeakerCategory.Player   => PlayerBrush,
            SpeakerCategory.Narrator => NarratorBrush,
            SpeakerCategory.Script   => ScriptBrush,
            _                        => NpcBrush
        } : NpcBrush;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
