using System.Globalization;
using DialogEditor.Core.Localisation;

namespace DialogEditor.Core.Resources;

/// <summary>
/// Public seam for setting the culture used by <see cref="CoreStrings"/> (the four
/// Core-layer strings: Script_Prefix_Enter/Exit/Update, Condition_Not).
/// Called at startup and on live language change.
/// </summary>
public static class CoreLocale
{
    // Cannot be localised even in principle: this is the code that installs the
    // culture, so it runs when culture selection has already failed.
    [NotLocalised("Debug diagnostic emitted while the locale system itself is failing")]
    public static void SetCulture(string? langCode)
    {
        if (langCode is null or "en")
        {
            CoreStrings.Culture = null;
            return;
        }
        try
        {
            CoreStrings.Culture = new CultureInfo(langCode);
        }
        catch (CultureNotFoundException ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"CoreLocale: unknown culture code '{langCode}': {ex.Message}. Falling back to English.");
            CoreStrings.Culture = null;
        }
    }
}
