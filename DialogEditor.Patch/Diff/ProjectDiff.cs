using System.Text.Json;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch.Diff;

/// Pure, value-aware diff of two project versions. Compares each conversation's
/// patches (no game files needed): a node is Added/Removed/Modified based on a
/// per-node signature of its full contribution to the patch.
///
/// NOTE — the diff is **patch-relative**, not effective-conversation-relative.
/// A node is "in" a version only if that version's patch contributes something
/// for it (an add/modify/delete/translation). Two consequences to be aware of:
///   • A node that version A's patch modifies but version B's patch leaves at the
///     game-base value is reported **Removed** (present in A's patch, absent from
///     B's) even though the node still exists in B — it has merely *reverted to
///     base*. On the canvas such a node is present and tinted (the ghost-injection
///     guard does not duplicate it). This is a known, accepted limitation; fixing
///     it would require reconstructing both conversations against the game base,
///     which would couple the (currently game-folder-free) list/diff to game data.
///   • `NodeComments` are **deliberately excluded** from the signature, so a
///     comment-only change produces no Added/Removed/Modified. This is intentional
///     and consistent with selective apply, which also treats node comments as
///     outside the apply unit (see the selective-apply design doc).
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

        // NodeComments are intentionally NOT a signature input (see class remarks):
        // a comment-only change is not surfaced as a node change, matching the
        // selective-apply decision to treat comments as outside the apply unit.
        var ids = added.Keys
            .Concat(modified.Keys).Concat(deleted).Concat(translations.Keys)
            .Distinct();

        foreach (var id in ids)
        {
            // The serialized object is order-sensitive (dictionaries/lists serialize
            // in insertion order). In practice patches come from the deterministic
            // DiffEngine, so value-equal patches always serialize identically; we
            // deliberately do NOT normalize ordering here, because some lists
            // (Conditions/Scripts) are order-significant and sorting them could mask
            // a real change.
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
