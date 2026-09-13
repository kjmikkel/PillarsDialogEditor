using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Converters;

/// <summary>Formats an autosave cadence in seconds (0, 30, 60, 300, ...) as its
/// localised option label ("Off", "30 seconds", "5 minutes") for the Settings picker
/// (issue #11). 0 is the "Off" sentinel. Goes through Loc rather than an inline format
/// string so the units and the singular/plural split stay translatable, and picks the
/// minute wording only for whole minutes so no option ever reads "1.5 minutes".</summary>
public sealed class AutosaveIntervalConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not int seconds) return null;
        if (seconds <= 0)      return Loc.Get("Settings_AutosaveInterval_Off");
        if (seconds % 60 != 0) return Loc.Format("Settings_AutosaveInterval_Seconds", seconds);
        var minutes = seconds / 60;
        return minutes == 1
            ? Loc.Get("Settings_AutosaveInterval_OneMinute")
            : Loc.Format("Settings_AutosaveInterval_Minutes", minutes);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
