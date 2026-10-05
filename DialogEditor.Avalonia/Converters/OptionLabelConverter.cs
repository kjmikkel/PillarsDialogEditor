using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Shows a dropdown item that is a stored enum name ("Bark", "MarkAsRead") as localised text
/// (issue 132). ConverterParameter is the key prefix: "Option_Display" + "Bark" gives the
/// label, "ToolTip_Option_Display" + "Bark" its tooltip. The item itself — what the
/// TwoWay SelectedItem writes back — stays the name, so a translated UI never stores a
/// translated word.
/// </summary>
/// <remarks>
/// "" (the property is absent) looks up the "NotSet" key. A name with no key of its own
/// (e.g. "Unknown(7)") uses "{prefix}_Fallback" with the name as {0} when that key exists,
/// and is otherwise shown exactly as stored.
/// </remarks>
public sealed class OptionLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string name || parameter is not string prefix) return value;

        if (Loc.TryGet(prefix + (name.Length == 0 ? "NotSet" : name), out var text)) return text;
        if (Loc.TryGet(prefix + "_Fallback", out var fallback)) return string.Format(fallback, name);
        return name;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Item text is display-only; the item is the stored name.");
}
