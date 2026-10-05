using DialogEditor.Core.Localisation;
using DialogEditor.Core.Logging;

namespace DialogEditor.Core.Backup;

/// <summary>
/// The folders a write is about to create, and their removal on restore (issue 125). Test
/// Patch (F5) and the patcher record these next to the files they create, so that restoring
/// returns the game folder to exactly how it was — not just its files.
/// </summary>
/// <remarks>
/// Only recorded folders are ever removed. Pruning "any empty parent" instead would also
/// delete folders the game ships empty, which the restore never created.
/// </remarks>
[NotLocalised("File-format and path handling only")]
public static class CreatedFolders
{
    /// The ancestors of <paramref name="filePath"/> that do not exist yet, deepest first —
    /// exactly what <see cref="Directory.CreateDirectory(string)"/> would create for it.
    /// Call before the write.
    public static IReadOnlyList<string> MissingAncestors(string filePath)
    {
        var missing = new List<string>();
        for (var dir = Path.GetDirectoryName(Path.GetFullPath(filePath));
             !string.IsNullOrEmpty(dir) && !Directory.Exists(dir);
             dir = Path.GetDirectoryName(dir))
            missing.Add(dir);
        return missing;
    }

    /// Removes each of <paramref name="folders"/> that exists and is empty. Called once per
    /// restore, after every file has been put back, with the folders recorded by
    /// <see cref="MissingAncestors"/> — possibly with duplicates and in any order.
    public static void RemoveIfEmpty(IEnumerable<string> folders)
    {
        // Longest path first: a child's full path is always longer than its parent's, so
        // each parent is checked only after its children have had their chance to go.
        foreach (var dir in folders.Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(d => d.Length))
        {
            try
            {
                if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Every file is already back by now, so a leftover empty folder (held open
                // by a scanner, say) is cosmetic: log it rather than fail the restore.
                AppLog.Warn($"Could not remove created folder '{dir}': {ex.Message}");
            }
        }
    }
}
