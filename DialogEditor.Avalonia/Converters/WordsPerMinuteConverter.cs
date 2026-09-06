using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Converters;

/// <summary>Formats a reading speed (200) as its localised option label ("200 wpm") for
/// the Flow Analytics reading-speed picker. Goes through Loc rather than an inline
/// format string so the unit stays translatable and the option list carries no
/// hardcoded text.</summary>
public sealed class WordsPerMinuteConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is int wpm ? Loc.Format("PathStats_WpmOption", wpm) : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
