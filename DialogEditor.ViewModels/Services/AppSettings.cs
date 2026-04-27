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
        catch { return new(); }
    }

    private static void Save(SettingsData data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(data));
        }
        catch { }
    }

    public static string PickLanguage(IReadOnlyList<string> available, string? preferred)
        => LanguagePicker.Pick(available, preferred);
}
