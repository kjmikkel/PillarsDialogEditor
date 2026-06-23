using System.IO;
using System.Text.Json;
using DialogEditor.Core.GameData;

namespace DialogEditor.ViewModels.Services;

public record PendingRestoreEntry(
    string BackupConvPath,
    string BackupStPath,
    string OriginalConvPath,
    string OriginalStPath);

public static class AppSettings
{
    private static readonly string _defaultSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "settings.json");

    // AsyncLocal so each test's override is isolated even when test classes run in parallel.
    private static readonly AsyncLocal<string?> _settingsPathOverride = new();
    internal static string? SettingsPathOverride
    {
        get => _settingsPathOverride.Value;
        set => _settingsPathOverride.Value = value;
    }

    private static string SettingsPath => SettingsPathOverride ?? _defaultSettingsPath;

    private sealed class SettingsData
    {
        public string? LastLanguage { get; set; }
        public string? LastGameDirectory { get; set; }
        public bool BrowserPinned  { get; set; } = true;
        public bool DetailExpanded { get; set; } = true;
        public HashSet<string> KnownGameDirectories  { get; set; } = [];
        public Dictionary<string, string> BackupPaths { get; set; } = [];
        public string? LastProjectPath               { get; set; }
        public List<PendingRestoreEntry>? PendingRestores { get; set; }
        public double? LegendX                       { get; set; }
        public double? LegendY                       { get; set; }
        public string DefaultLocalizationFormat      { get; set; } = "Csv";
        // Layer 2 (runtime theming): the selected palette id (see ThemeApplier catalog), or
        // "Auto" to follow the OS dark/light/high-contrast preference (resolved by
        // ThemeApplier.DetectOsThemeId). "Auto" is the default for fresh installs; existing
        // installs keep whatever they already had persisted (e.g. the historical "Dark").
        public string Theme                          { get; set; } = "Auto";
        // The font-scale multiplier applied to every FontSize.* token at next startup
        // (Gaps item 6 part B). 1.0 = no scaling, the historical/default size. Changing
        // this only persists the value; FontScaleApplier applies it once at the next
        // launch, so already-open windows are unaffected until restart.
        public double FontScale                      { get; set; } = 1.0;
        // Whether the first-run theme-onboarding dialog (Gaps item 15) has been shown.
        // Defaults to true so that EXISTING installs upgrading to this version (whose
        // settings.json predates this field) silently treat onboarding as already-seen —
        // only a genuinely fresh install (no settings.json yet, or a load failure) gets
        // false, via Load().
        public bool ThemeOnboardingSeen              { get; set; } = true;
        // Whether the in-app guided tour has been seen (or deliberately dismissed).
        // Defaults to true so that EXISTING installs upgrading to this version silently
        // treat the tour as already-seen — only a genuinely fresh install (no settings.json
        // yet, or a load failure) gets false, via Load().
        public bool GuidedTourSeen                   { get; set; } = true;
        // UI language code (BCP-47, e.g. "en", "de"). Defaults to "en" (English).
        // TODO: add "Auto" (OS locale detection) once a non-English translation ships —
        //       would resolve via CultureInfo.CurrentUICulture and fall back to "en".
        public string UiLanguage                     { get; set; } = "en";
    }

    public static string? LastLanguage
    {
        get => Load().LastLanguage;
        set { var s = Load(); s.LastLanguage = value; Save(s); }
    }

    public static string? LastGameDirectory
    {
        get => Load().LastGameDirectory;
        set { var s = Load(); s.LastGameDirectory = value; Save(s); }
    }

    public static bool BrowserPinned
    {
        get => Load().BrowserPinned;
        set { var s = Load(); s.BrowserPinned = value; Save(s); }
    }

    public static bool DetailExpanded
    {
        get => Load().DetailExpanded;
        set { var s = Load(); s.DetailExpanded = value; Save(s); }
    }

    private static SettingsData Load()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new() { ThemeOnboardingSeen = false, GuidedTourSeen = false };
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<SettingsData>(json) ?? new();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to load settings from {SettingsPath}: {ex.Message}");
            return new() { ThemeOnboardingSeen = false, GuidedTourSeen = false };
        }
    }

    private static void Save(SettingsData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data));
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to save settings to {SettingsPath}: {ex.Message}");
        }
    }

    public static bool IsKnownGameDirectory(string path)
        => Load().KnownGameDirectories.Contains(path);

    public static void MarkGameDirectoryKnown(string path)
    {
        var s = Load();
        s.KnownGameDirectories.Add(path);
        Save(s);
    }

    public static string? GetBackupPath(string gameDirectory)
        => Load().BackupPaths.GetValueOrDefault(gameDirectory);

    public static void SetBackupPath(string gameDirectory, string backupRoot)
    {
        var s = Load();
        s.BackupPaths[gameDirectory] = backupRoot;
        Save(s);
    }

    public static string? LastProjectPath
    {
        get => Load().LastProjectPath;
        set { var s = Load(); s.LastProjectPath = value; Save(s); }
    }

    public static IReadOnlyList<PendingRestoreEntry>? GetPendingRestores()
        => Load().PendingRestores;

    public static void SetPendingRestores(IEnumerable<PendingRestoreEntry> entries)
    {
        var s = Load();
        s.PendingRestores = entries.ToList();
        Save(s);
    }

    public static void ClearPendingRestores()
    {
        var s = Load();
        s.PendingRestores = null;
        Save(s);
    }

    public static (double X, double Y)? GetLegendPosition()
    {
        var s = Load();
        return s.LegendX is double x && s.LegendY is double y ? (x, y) : null;
    }

    public static void SetLegendPosition(double x, double y)
    {
        var s = Load();
        s.LegendX = x;
        s.LegendY = y;
        Save(s);
    }

    public static string DefaultLocalizationFormat
    {
        get => Load().DefaultLocalizationFormat;
        set { var s = Load(); s.DefaultLocalizationFormat = value; Save(s); }
    }

    public static string Theme
    {
        get => Load().Theme;
        set { var s = Load(); s.Theme = value; Save(s); }
    }

    public static double FontScale
    {
        get => Load().FontScale;
        set { var s = Load(); s.FontScale = value; Save(s); }
    }

    public static bool ThemeOnboardingSeen
    {
        get => Load().ThemeOnboardingSeen;
        set { var s = Load(); s.ThemeOnboardingSeen = value; Save(s); }
    }

    public static bool GuidedTourSeen
    {
        get => Load().GuidedTourSeen;
        set { var s = Load(); s.GuidedTourSeen = value; Save(s); }
    }

    public static string UiLanguage
    {
        get => Load().UiLanguage;
        set { var s = Load(); s.UiLanguage = value; Save(s); }
    }

    public static string PickLanguage(IReadOnlyList<string> available, string? preferred)
        => LanguagePicker.Pick(available, preferred);
}
