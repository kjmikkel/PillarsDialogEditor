using System.Security.Cryptography;
using System.Text;
using DialogEditor.Core.Localisation;

namespace DialogEditor.Core.Backup;

/// <summary>One write the editor made into the game folder: the file's content before
/// (<c>null</c> = it did not exist) and after, as SHA-256 hex.</summary>
public sealed record EditorWrite(string Path, string? BeforeSha256, string AfterSha256);

public enum RestoreSkipReason
{
    /// Something other than the editor changed or removed the file since the snapshot —
    /// usually a game update or a storefront "verify files".
    ChangedOutsideEditor,

    /// The patcher (dialog-patcher / Patch Manager) manages the file; its own Restore is
    /// the right undo, and it may hold a newer original than the snapshot.
    ManagedByPatcher,
}

public sealed record RestoreSkip(string Path, RestoreSkipReason Reason);

public sealed record FullRestoreResult(IReadOnlyList<string> Restored, IReadOnlyList<RestoreSkip> Skipped);

/// <summary>
/// Restore Full Backup (issue 118): copies files back from the one-time snapshot taken when
/// a game folder was first opened, without ever overwriting a file the editor didn't write.
/// </summary>
/// <remarks>
/// The snapshot can be older than the game's data: a game patch or "verify files" after it
/// was taken would be silently downgraded by a blind copy. So a snapshot file is copied back
/// only when the live file's current content is an editor write (see
/// <see cref="EditorWriteJournal"/>) that was made on top of exactly the snapshot's copy.
/// Requiring both hashes matters: if the game updated first and the editor then wrote over
/// the update, the snapshot is the wrong original and copying it would undo the update too.
/// Everything else that differs is left alone and reported. Files only in the live folder
/// are not touched: the editor's own new files are F6's job, anything else came from the game.
/// </remarks>
[NotLocalised("File-format and path handling only")]
public static class FullBackupRestore
{
    public static FullRestoreResult Restore(
        string snapshotRoot,
        string liveRoot,
        IReadOnlyCollection<EditorWrite> editorWrites,
        IReadOnlySet<string> patcherManaged)
    {
        var writesByPath = editorWrites
            .GroupBy(w => Normalise(w.Path), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);
        var managed = patcherManaged.Select(Normalise).ToHashSet(StringComparer.OrdinalIgnoreCase);

        var restored = new List<string>();
        var skipped  = new List<RestoreSkip>();
        if (!Directory.Exists(snapshotRoot)) return new FullRestoreResult(restored, skipped);

        foreach (var snapFile in Directory.EnumerateFiles(snapshotRoot, "*", SearchOption.AllDirectories))
        {
            var live     = Normalise(Path.Combine(liveRoot, Path.GetRelativePath(snapshotRoot, snapFile)));
            var snapHash = FileHash.Of(snapFile);
            var liveHash = FileHash.Of(live);

            if (liveHash == snapHash) continue;   // already the snapshot's content

            if (managed.Contains(live))
            {
                skipped.Add(new RestoreSkip(live, RestoreSkipReason.ManagedByPatcher));
                continue;
            }

            var editorWroteOverSnapshot = liveHash is not null
                && writesByPath.TryGetValue(live, out var writes)
                && writes.Any(w => w.AfterSha256 == liveHash && w.BeforeSha256 == snapHash);
            if (!editorWroteOverSnapshot)
            {
                skipped.Add(new RestoreSkip(live, RestoreSkipReason.ChangedOutsideEditor));
                continue;
            }

            File.Copy(snapFile, live, overwrite: true);
            restored.Add(live);
        }
        return new FullRestoreResult(restored, skipped);
    }

    private static string Normalise(string path) => Path.GetFullPath(path);
}

/// <summary>SHA-256 hex of a file's bytes, or <c>null</c> when it does not exist.</summary>
[NotLocalised("File-format and path handling only")]
public static class FileHash
{
    public static string? Of(string path) =>
        File.Exists(path) ? Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))) : null;

    public static string OfText(string content) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
}
