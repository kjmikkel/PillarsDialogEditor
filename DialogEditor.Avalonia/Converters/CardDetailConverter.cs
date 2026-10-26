using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;
using DialogEditor.Core.Layout;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Maps the canvas zoom (NodifyEditor.ViewportZoom) to a card's detail level (GitHub issue 221):
/// ConverterParameter "lines" gives the body's MaxLines, "footer" whether the footer shows.
/// </summary>
public sealed class CardDetailConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var zoom = value is double d ? d : 1.0;
        return parameter as string == "footer"
            ? CardDetail.ShowsFooterAt(zoom)
            : CardDetail.BodyLinesAt(zoom);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
