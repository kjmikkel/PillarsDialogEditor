using System.IO;
using System.Text.Json;
using DialogEditor.Core.GameData;

namespace DialogEditor.ViewModels.Services;

public static class AppSettings
{
    private static readonly string SettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "settings.json");

    private sealed class SettingsData
    {
        public string? LastLanguage { get; set; }
        public string? LastGameDirectory { get; set; }
        public bool BrowserPinned  { get; set; } = true;
        public bool DetailExpanded { get; set; } = true;
        public HashSet<string> KnownGameDirectories { get; set; } = [];
        public Dictionary<string, string> BackupPaths { get; set; } = [];
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
            if (!File.Exists(SettingsPath)) return new();
            var json = File.ReadAllText(SettingsPath);
            return JsonSerializer.Deserialize<SettingsData>(json) ?? new();
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to load settings from {SettingsPath}: {ex.Message}");
            return new();
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

    public static string PickLanguage(IReadOnlyList<string> available, string? preferred)
        => LanguagePicker.Pick(available, preferred);
}
