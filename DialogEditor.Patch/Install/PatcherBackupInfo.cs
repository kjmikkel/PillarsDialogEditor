using DialogEditor.Core.Logging;

namespace DialogEditor.Patch.Install;

/// Read-only view of the patcher's backup for the editor (issue #76): the editor never writes
/// the patcher's manifest, it only asks "are mods installed over these files?". Merging the
/// two backup systems is #66's job.
public sealed class PatcherBackupInfo
{
    private readonly PatcherBackupStore? _store;

    private PatcherBackupInfo(PatcherBackupStore? store) => _store = store;

    /// True when a manifest exists but cannot be read — callers should assume the worst.
    public bool IsUnknown => _store is null;

    public static PatcherBackupInfo? TryOpen(string gameDir)
    {
        if (string.IsNullOrEmpty(gameDir) || !PatcherBackupStore.Exists(gameDir)) return null;
        try
        {
            return new PatcherBackupInfo(PatcherBackupStore.Open(gameDir));
        }
        catch (PatcherBackupCorruptException ex)
        {
            AppLog.Error("Patcher backup manifest unreadable; assuming it covers every file", ex);
            return new PatcherBackupInfo(null);
        }
    }

    public IReadOnlyList<string> CoveredPaths(IEnumerable<string> absPaths)
    {
        if (_store is null) return absPaths.ToList();
        var managed = _store.Entries.Select(e => e.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return absPaths.Where(p =>
        {
            try { return managed.Contains(_store.ToRelative(p)); }
            catch (ArgumentException ex)
            {
                AppLog.Warn($"Path outside game folder ignored for patcher check: {p} ({ex.Message})");
                return false;
            }
        }).ToList();
    }
}
