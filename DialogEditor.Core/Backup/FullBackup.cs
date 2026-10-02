using System.Text.Json;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Localisation;
using DialogEditor.Core.Logging;

namespace DialogEditor.Core.Backup;

/// <summary>What a full backup folder holds. Format 2 (issue 123) adds the per-language
/// stringtable folders; a backup without a manifest is format 1 (one flat stringtables folder).</summary>
public sealed record FullBackupManifest(int Format, string GameId, IReadOnlyList<string> Languages);

/// <summary>
/// The on-disk layout of the full backup (issues 118, 123): conversations, one stringtables
/// folder per installed language, and a backup.json manifest. Restoring each snapshot/live
/// folder pair is still <see cref="FullBackupRestore"/>'s job, so its "only undo editor
/// writes" guard applies unchanged.
/// </summary>
[NotLocalised("File-format and path handling only")]
public static class FullBackup
{
    public const string ManifestFileName = "backup.json";
    private const int CurrentFormat = 2;

    private static readonly JsonSerializerOptions ManifestJson = new() { WriteIndented = true };

    public static async Task TakeAsync(IGameDataProvider provider, string backupRoot, CancellationToken ct)
    {
        await BackupService.BackupAsync(provider.GetBackupRoots().ConversationsRoot,
            Path.Combine(backupRoot, "conversations"), ct);

        var languages = new List<string>();
        foreach (var lang in provider.AvailableLanguages.OrderBy(l => l, StringComparer.Ordinal))
        {
            var source = provider.GetStringTablesRoot(lang);
            if (!Directory.Exists(source)) continue;
            await BackupService.BackupAsync(source, Path.Combine(backupRoot, "stringtables", lang), ct);
            languages.Add(lang);
        }

        // Written last on purpose: a backup interrupted part-way then has no manifest and is
        // treated as an untrusted format-1 backup rather than a complete format-2 one.
        File.WriteAllText(Path.Combine(backupRoot, ManifestFileName),
            JsonSerializer.Serialize(new FullBackupManifest(CurrentFormat, provider.GameId, languages), ManifestJson));
    }

    /// <summary>The backup's manifest, or <c>null</c> for a format-1 backup (none) or an unreadable one.</summary>
    public static FullBackupManifest? ReadManifest(string backupRoot)
    {
        var path = Path.Combine(backupRoot, ManifestFileName);
        if (!File.Exists(path)) return null;
        try
        {
            return JsonSerializer.Deserialize<FullBackupManifest>(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Backup manifest {ManifestFileName} in {backupRoot} is unreadable, treating it as an older backup: {ex.Message}");
            return null;
        }
    }

    /// <summary>The newest timestamped backup under the folder the player picked, or <c>null</c>.</summary>
    public static string? Latest(string backupPick) =>
        Directory.Exists(backupPick)
            ? Directory.GetDirectories(backupPick).OrderByDescending(d => d, StringComparer.Ordinal).FirstOrDefault()
            : null;
}
