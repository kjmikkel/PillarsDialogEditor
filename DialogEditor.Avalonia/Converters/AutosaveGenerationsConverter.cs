using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Converters;

/// <summary>Formats an autosave generation count as its localised option label
/// ("1 copy", "3 copies") for the Settings picker (issue #11). The singular is a
/// separate resource rather than a suffix rule, because pluralisation is not a
/// suffix in every language this may be translated into.</summary>
public sealed class AutosaveGenerationsConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is not int count ? null
         : count == 1 ? Loc.Get("Settings_AutosaveGenerations_One")
         : Loc.Format("Settings_AutosaveGenerations_Many", count);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
