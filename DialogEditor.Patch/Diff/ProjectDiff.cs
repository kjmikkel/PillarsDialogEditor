using System.Text.Json;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch.Diff;

/// Pure, value-aware diff of two project versions. Compares each conversation's
/// patches (no game files needed): a node is Added/Removed/Modified based on a
/// per-node signature of its full contribution to the patch.
public static class ProjectDiff
{
    public static List<ConversationChange> Diff(DialogProject a, DialogProject b)
    {
        var changes = new List<ConversationChange>();
        var names = a.Patches.Keys.Concat(b.Patches.Keys).Distinct();

        foreach (var name in names)
        {
            var sigA = Signatures(a.Patches.GetValueOrDefault(name));
            var sigB = Signatures(b.Patches.GetValueOrDefault(name));

            var added    = sigB.Keys.Where(id => !sigA.ContainsKey(id)).OrderBy(id => id).ToList();
            var removed  = sigA.Keys.Where(id => !sigB.ContainsKey(id)).OrderBy(id => id).ToList();
            var modified = sigB.Keys
                .Where(id => sigA.TryGetValue(id, out var s) && !string.Equals(s, sigB[id], StringComparison.Ordinal))
                .OrderBy(id => id).ToList();

            if (added.Count + removed.Count + modified.Count > 0)
                changes.Add(new ConversationChange(name, added, removed, modified));
        }

        return changes.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    // nodeId -> JSON signature of that node's full contribution to the patch.
    private static Dictionary<int, string> Signatures(ConversationPatch? patch)
    {
        var map = new Dictionary<int, string>();
        if (patch is null) return map;

        var added = new Dictionary<int, NodeEditSnapshot>();
        foreach (var n in patch.AddedNodes) added[n.NodeId] = n;

        var modified = new Dictionary<int, NodeModification>();
        foreach (var m in patch.ModifiedNodes) modified[m.NodeId] = m;

        var deleted = patch.DeletedNodeIds.ToHashSet();

        var translations = new Dictionary<int, List<NodeTranslation>>();
        foreach (var (lang, list) in patch.Translations)
            foreach (var t in list)
                (translations.TryGetValue(t.NodeId, out var l) ? l : translations[t.NodeId] = []).Add(t);

        var ids = added.Keys
            .Concat(modified.Keys).Concat(deleted).Concat(translations.Keys)
            .Distinct();

        foreach (var id in ids)
        {
            map[id] = JsonSerializer.Serialize(new
            {
                Added    = added.GetValueOrDefault(id),
                Modified = modified.GetValueOrDefault(id),
                Deleted  = deleted.Contains(id),
                Text     = translations.GetValueOrDefault(id),
            });
        }
        return map;
    }
}
