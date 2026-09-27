using System.Security.Cryptography;
using System.Text.Json;
using DialogEditor.Core.Localisation;
using DialogEditor.Core.Logging;

namespace DialogEditor.Patch.Install;

/// <summary>
/// The patcher's own safety net, kept inside the game folder (issue #76) so the CLI, the
/// Patch Manager and the editor all find it from the game directory alone, with no settings.
///
/// Files are backed up lazily, the first time the patcher is about to write each one, so the
/// backup holds only what mods actually touch. Invariant: the manifest is saved *before* each
/// game-file write, so at every instant it lists every file the patcher might have changed —
/// a crash mid-apply can always be undone.
/// </summary>
[NotLocalised("File-format and path handling only")]
public sealed class PatcherBackupStore
{
    public const string FolderName       = "PillarsDialogPatcher";
    public const string ManifestFileName = "manifest.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly List<BackupEntry> _entries;

    private PatcherBackupStore(string gameDir, List<BackupEntry> entries)
    {
        GameDir  = Path.GetFullPath(gameDir);
        _entries = entries;
    }

    public string GameDir      { get; }
    public string BackupRoot   => Path.Combine(GameDir, FolderName);
    public string ManifestPath => Path.Combine(BackupRoot, ManifestFileName);
    private string FilesRoot   => Path.Combine(BackupRoot, "files");

    public IReadOnlyList<BackupEntry> Entries => _entries;

    public static bool Exists(string gameDir) =>
        File.Exists(Path.Combine(gameDir, FolderName, ManifestFileName));

    public static PatcherBackupStore Open(string gameDir)
    {
        var manifestPath = Path.Combine(Path.GetFullPath(gameDir), FolderName, ManifestFileName);
        if (!File.Exists(manifestPath))
            return new PatcherBackupStore(gameDir, []);

        PatcherManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<PatcherManifest>(File.ReadAllText(manifestPath));
        }
        catch (JsonException ex)
        {
            throw new PatcherBackupCorruptException(manifestPath, "invalid JSON", ex);
        }

        if (manifest is null || manifest.Entries is null)
            throw new PatcherBackupCorruptException(manifestPath, "empty manifest");
        if (manifest.SchemaVersion is < 1 or > PatcherManifest.CurrentSchemaVersion)
            throw new PatcherBackupCorruptException(manifestPath,
                $"unsupported schema version {manifest.SchemaVersion}");

        return new PatcherBackupStore(gameDir, [.. manifest.Entries]);
    }

    public void EnsureBackedUp(string absPath)
    {
        var rel = ToRelative(absPath);
        if (IndexOf(rel) >= 0) return;   // idempotent: never back up already-modded bytes

        if (File.Exists(absPath))
        {
            var copy = BackupCopyPath(rel);
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(absPath, copy, overwrite: true);   // copy first: the manifest never points at a missing copy
            _entries.Add(new BackupEntry(rel, BackupEntryKind.Overwritten, HashFile(absPath), null));
        }
        else
        {
            _entries.Add(new BackupEntry(rel, BackupEntryKind.Created, null, null));
        }
        Save();
    }

    public void RecordWritten(string absPath)
    {
        var i = IndexOf(ToRelative(absPath));
        if (i < 0) throw new InvalidOperationException($"'{absPath}' was written without being backed up first.");
        _entries[i] = _entries[i] with { LastWrittenSha256 = HashFile(absPath) };
        Save();
    }

    public string ToRelative(string absPath)
    {
        var rel = Path.GetRelativePath(GameDir, Path.GetFullPath(absPath));
        if (rel.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(rel))
            throw new ArgumentException($"'{absPath}' is outside the game directory '{GameDir}'.", nameof(absPath));
        return rel.Replace('\\', '/');
    }

    public string ToAbsolute(string relPath) =>
        Path.Combine(GameDir, relPath.Replace('/', Path.DirectorySeparatorChar));

    public static string? HashFile(string absPath) =>
        File.Exists(absPath) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(absPath))) : null;

    /// A file's "original state" is its backed-up hash (Overwritten) or absence (Created);
    /// null stands for "file absent" throughout.
    private static string? OriginalState(BackupEntry e) =>
        e.Kind == BackupEntryKind.Overwritten ? e.OriginalSha256 : null;

    private bool IsExternallyChanged(BackupEntry e)
    {
        var current = HashFile(ToAbsolute(e.Path));
        if (current == OriginalState(e)) return false;
        if (e.LastWrittenSha256 is not null && current == e.LastWrittenSha256) return false;
        return true;
    }

    public IReadOnlyList<string> FindExternallyChanged() =>
        _entries.Where(IsExternallyChanged).Select(e => e.Path).ToList();

    public IReadOnlyList<string> RestoreAll()
    {
        var skipped = new List<string>();
        for (var i = 0; i < _entries.Count; i++)
        {
            var e = _entries[i];
            if (IsExternallyChanged(e)) { skipped.Add(e.Path); continue; }

            var abs = ToAbsolute(e.Path);
            if (e.Kind == BackupEntryKind.Overwritten)
            {
                if (HashFile(abs) != e.OriginalSha256)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(abs)!);
                    File.Copy(BackupCopyPath(e.Path), abs, overwrite: true);
                }
            }
            else if (File.Exists(abs))
            {
                File.Delete(abs);
            }
            _entries[i] = e with { LastWrittenSha256 = null };
        }
        Save();
        return skipped;
    }

    public void AcceptCurrentAsOriginal(IEnumerable<string> relPaths)
    {
        foreach (var rel in relPaths)
        {
            var i = IndexOf(rel);
            if (i < 0) continue;
            var abs  = ToAbsolute(_entries[i].Path);
            var copy = BackupCopyPath(_entries[i].Path);
            if (File.Exists(abs))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
                File.Copy(abs, copy, overwrite: true);
                _entries[i] = _entries[i] with
                {
                    Kind = BackupEntryKind.Overwritten, OriginalSha256 = HashFile(abs), LastWrittenSha256 = null,
                };
            }
            else
            {
                if (File.Exists(copy)) File.Delete(copy);
                _entries[i] = _entries[i] with
                {
                    Kind = BackupEntryKind.Created, OriginalSha256 = null, LastWrittenSha256 = null,
                };
            }
        }
        Save();
    }

    public void DeleteBackup()
    {
        if (Directory.Exists(BackupRoot))
            Directory.Delete(BackupRoot, recursive: true);
        _entries.Clear();
    }

    private string BackupCopyPath(string rel) =>
        Path.Combine(FilesRoot, rel.Replace('/', Path.DirectorySeparatorChar));

    private int IndexOf(string rel) =>
        _entries.FindIndex(e => string.Equals(e.Path, rel, StringComparison.OrdinalIgnoreCase));

    /// Atomic: write a sibling temp file, then move it over the manifest, so a crash leaves
    /// either the old or the new manifest, never a torn one.
    private void Save()
    {
        Directory.CreateDirectory(BackupRoot);
        var manifest = new PatcherManifest(
            PatcherManifest.CurrentSchemaVersion,
            AppVersion.FromAssembly(typeof(PatcherBackupStore).Assembly),
            _entries);
        var tmp = ManifestPath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(manifest, JsonOptions));
        MoveWithRetry(tmp, ManifestPath);
    }

    /// Replacing a file that was written a moment ago can fail with "access denied" on Windows
    /// while an antivirus scanner or the search indexer still holds it open — seen in practice
    /// on the test suite, and game folders are scanned just the same. Such locks last
    /// milliseconds, so retry briefly before giving up.
    private static void MoveWithRetry(string source, string dest)
    {
        const int attempts = 6;
        for (var i = 1; ; i++)
        {
            try
            {
                File.Move(source, dest, overwrite: true);
                return;
            }
            catch (Exception ex) when (i < attempts && ex is UnauthorizedAccessException or IOException)
            {
                AppLog.Warn($"Replacing '{dest}' failed (attempt {i} of {attempts}), retrying: {ex.Message}");
                Thread.Sleep(25 * i);
            }
        }
    }
}
