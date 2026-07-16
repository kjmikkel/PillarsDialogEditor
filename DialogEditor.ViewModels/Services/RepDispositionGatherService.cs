using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels.Services;

public enum BalanceSource { ProjectChanges, OnDiskPlusChanges }
public enum BalanceScope  { Current, All }

/// Resolves a Source×Scope selection into (name, effective-snapshot) pairs for the tally.
/// Mirrors ProjectFindService: the open conversation uses its live snapshot, other patched
/// conversations are base+patch, and (for the on-disk source) every provider conversation is
/// included with its patch applied when one exists. Unreadable conversations are logged and
/// skipped so one bad file never aborts the analysis.
public static class RepDispositionGatherService
{
    public static IReadOnlyList<(string Name, ConversationEditSnapshot Snapshot)> Gather(
        BalanceSource source, BalanceScope scope,
        DialogProject project, IGameDataProvider provider,
        string? openName, ConversationEditSnapshot? openSnapshot,
        CancellationToken ct = default)
    {
        var result = new List<(string, ConversationEditSnapshot)>();

        if (scope == BalanceScope.Current)
        {
            if (openSnapshot is not null && openName is not null)
                result.Add((openName, openSnapshot));
            return result;
        }

        ConversationEditSnapshot? Effective(string name)
        {
            if (name == openName && openSnapshot is not null) return openSnapshot;
            try
            {
                var file = provider.FindConversation(name);
                var baseSnap = file is not null
                    ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                    : new ConversationEditSnapshot([]);
                return project.Patches.TryGetValue(name, out var patch)
                    ? PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true)
                    : baseSnap;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Rep/disposition balance: could not load '{name}': {ex.Message}");
                return null;
            }
        }

        IEnumerable<string> names = source == BalanceSource.ProjectChanges
            ? project.Patches.Keys
            : provider.EnumerateConversations().Select(f => f.Name);

        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();
            if (Effective(name) is { } snap) result.Add((name, snap));
        }
        return result;
    }
}
