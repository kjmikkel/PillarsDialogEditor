using System.Text.Json;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Localisation;
using DialogEditor.Core.Logging;

namespace DialogEditor.Core.Backup;

/// <summary>What a full backup folder holds. Format 2 (issue 123) adds the per-language
/// stringtable folders; a backup without a manifest is format 1 (one flat stringtables folder).</summary>
public sealed record FullBackupManifest(int Format, string GameId, IReadOnlyList<string> Languages);

/// <summary>One snapshot folder and the live folder it restores into.</summary>
public sealed record RestorePair(string Snapshot, string Live);

/// <summary>What to restore, and whether an older backup's text had to be left out because
/// its language couldn't be determined.</summary>
public sealed record RestorePlan(IReadOnlyList<RestorePair> Pairs, bool TextLanguageUnknown);

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
    private const string VoiceOverFolder = "voice-over";

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
            // Valid JSON can still lack the language list (truncated, hand-edited); without it
            // the manifest says nothing usable, so it is treated like a missing one.
            if (JsonSerializer.Deserialize<FullBackupManifest>(File.ReadAllText(path)) is { Languages: not null } manifest)
                return manifest;
            AppLog.Warn($"Backup manifest {ManifestFileName} in {backupRoot} lists no languages, treating it as an older backup");
            return null;
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

    /// <summary>
    /// Which snapshot folder restores into which live folder. Format 2 names its languages in
    /// the manifest; format 1 has one unlabelled stringtables folder, whose language is
    /// inferred (never assumed from the language currently selected — issue 123).
    /// </summary>
    public static RestorePlan PlanRestore(IGameDataProvider provider, string backupRoot, string? voicesRoot)
    {
        var pairs = new List<RestorePair>
        {
            new(Path.Combine(backupRoot, "conversations"), provider.GetBackupRoots().ConversationsRoot),
        };
        var textLanguageUnknown = false;

        if (ReadManifest(backupRoot) is { } manifest)
        {
            var installed = provider.AvailableLanguages.ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var lang in manifest.Languages.Where(installed.Contains))
            {
                var live = provider.GetStringTablesRoot(lang);
                if (Directory.Exists(live))
                    pairs.Add(new RestorePair(Path.Combine(backupRoot, "stringtables", lang), live));
            }
        }
        else
        {
            var flat = Path.Combine(backupRoot, "stringtables");
            if (Directory.Exists(flat))
            {
                if (InferLegacyLanguage(provider, flat) is { } lang)
                    pairs.Add(new RestorePair(flat, provider.GetStringTablesRoot(lang)));
                else
                    textLanguageUnknown = true;
            }
        }

        // In any format: voice-over/ is only ever written by PreserveOriginal, into whichever
        // backup is latest — and every backup taken before issue 123 is format 1.
        var voiceSnapshot = Path.Combine(backupRoot, VoiceOverFolder);
        if (voicesRoot is not null && Directory.Exists(voiceSnapshot) && Directory.Exists(voicesRoot))
            pairs.Add(new RestorePair(voiceSnapshot, voicesRoot));

        return new RestorePlan(pairs, textLanguageUnknown);
    }

    /// <summary>
    /// The language an older (single-folder) backup was taken in: the installed language whose
    /// live stringtables most often equal the snapshot's, provided it has at least one match
    /// and no other language ties it. Translations differ, so only the original language
    /// matches. Read-only; <c>null</c> means unknown.
    /// </summary>
    public static string? InferLegacyLanguage(IGameDataProvider provider, string legacyStringTablesRoot)
    {
        if (!Directory.Exists(legacyStringTablesRoot)) return null;
        var snapshot = Directory.EnumerateFiles(legacyStringTablesRoot, "*", SearchOption.AllDirectories)
            .Select(f => (Relative: Path.GetRelativePath(legacyStringTablesRoot, f), Hash: FileHash.Of(f)))
            .ToList();

        var scores = provider.AvailableLanguages
            .Select(lang =>
            {
                var live = provider.GetStringTablesRoot(lang);
                return (Lang: lang, Matches: snapshot.Count(s => s.Hash == FileHash.Of(Path.Combine(live, s.Relative))));
            })
            .ToList();

        var best = scores.Select(s => s.Matches).DefaultIfEmpty(0).Max();
        var winners = scores.Where(s => s.Matches == best).ToList();
        return best > 0 && winners.Count == 1 ? winners[0].Lang : null;
    }

    /// <summary>
    /// Keeps the original of a shipped file the editor is about to overwrite (Test Patch's
    /// voice-over sync), under <c>voice-over/</c> in the backup. Only the first time: a copy
    /// already there is the true original, and a later one could be the editor's own output.
    /// Returns <c>false</c> (and logs) when it couldn't, e.g. the backup folder is unreachable;
    /// the caller carries on, as the test itself doesn't depend on the backup.
    /// </summary>
    public static bool PreserveOriginal(string backupRoot, string liveRoot, string liveFile)
    {
        try
        {
            var target = Path.Combine(backupRoot, VoiceOverFolder, Path.GetRelativePath(liveRoot, liveFile));
            if (File.Exists(target)) return true;
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(liveFile, target);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn($"Could not keep the original of {liveFile} in the backup: {ex.Message}");
            return false;
        }
    }
}
