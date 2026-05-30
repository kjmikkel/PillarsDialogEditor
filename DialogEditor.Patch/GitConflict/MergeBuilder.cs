using DialogEditor.Core.Models;

namespace DialogEditor.Patch.GitConflict;

/// Produces the merged project from the user's per-conflict resolutions.
///
/// Because the reconstructed sides are identical outside git's conflict hunks,
/// the merge starts from the "mine" project and overlays only the values for
/// conflicts the user resolved to "theirs". Conversations present in only one
/// side are carried through unchanged.
public static class MergeBuilder
{
    private const string DeletedMarker = MergeConflict.DeletedMarker;

    // Takes a list of (conflict, chosen side) pairs rather than a dictionary so we
    // don't depend on MergeConflict's value-equality as a hash key (two structurally
    // identical conflicts must not collapse into one entry).
    public static DialogProject Build(
        DialogProject mine,
        DialogProject theirs,
        IReadOnlyList<(MergeConflict Conflict, MergeSide Side)> choices)
    {
        var patches = new Dictionary<string, ConversationPatch>(mine.Patches);

        // Carry through conversations that exist only on the "theirs" side.
        foreach (var (conv, theirPatch) in theirs.Patches)
            if (!patches.ContainsKey(conv))
                patches[conv] = theirPatch;

        // Apply theirs-resolved conflicts, grouped per conversation.
        var theirsResolved = choices
            .Where(c => c.Side == MergeSide.Theirs)
            .Select(c => c.Conflict)
            .GroupBy(c => c.ConversationName);

        foreach (var group in theirsResolved)
        {
            var conv = group.Key;
            patches[conv] = ApplyToPatch(mine.Patches[conv], theirs.Patches[conv], group);
        }

        var result = mine with { Patches = patches };

        // Fold in theirs' canvas layout positions (per-node union; theirs wins on
        // overlap, matching DialogProject.MergeLayout) — cosmetic, auto-merged.
        foreach (var (conv, positions) in theirs.Layouts
                 ?? new Dictionary<string, IReadOnlyDictionary<int, LayoutPoint>>())
            result = result.MergeLayout(conv, positions);

        // Union the new-conversation lists — additive, so a conversation either side
        // created is never lost.
        var newConvs = (mine.NewConversations ?? [])
            .Concat(theirs.NewConversations ?? [])
            .Distinct()
            .ToList();

        return newConvs.Count > 0 ? result with { NewConversations = newConvs } : result;
    }

    private static ConversationPatch ApplyToPatch(
        ConversationPatch minePatch,
        ConversationPatch theirPatch,
        IEnumerable<MergeConflict> theirsConflicts)
    {
        var modifiedById = ById(minePatch.ModifiedNodes, m => m.NodeId);
        var addedById    = ById(minePatch.AddedNodes,    n => n.NodeId);
        var deleted      = minePatch.DeletedNodeIds.ToHashSet();

        var theirModById   = ById(theirPatch.ModifiedNodes, m => m.NodeId);
        var theirAddedById = ById(theirPatch.AddedNodes,    n => n.NodeId);

        // Mutable copy of mine's translations (lang -> list), plus a lookup of
        // theirs' translations by (nodeId, lang) for TranslationEdit overlays.
        var translations = minePatch.Translations.ToDictionary(kv => kv.Key, kv => kv.Value.ToList());
        var theirTr = new Dictionary<(int NodeId, string Lang), NodeTranslation>();
        foreach (var (lang, list) in theirPatch.Translations)
            foreach (var t in list)
                theirTr[(t.NodeId, lang)] = t;

        foreach (var c in theirsConflicts)
        {
            switch (c.Kind)
            {
                case MergeConflictKind.FieldEdit:
                    modifiedById[c.NodeId] = ApplyField(
                        modifiedById[c.NodeId], theirModById[c.NodeId], c.FieldName!);
                    break;

                case MergeConflictKind.TranslationEdit:
                    if (theirTr.TryGetValue((c.NodeId, c.FieldName!), out var theirText))
                        SetTranslation(translations, c.FieldName!, theirText);
                    break;

                case MergeConflictKind.DeleteVsEdit:
                    if (c.TheirsValue == DeletedMarker)
                    {
                        // theirs deletes — drop the node, record the deletion
                        modifiedById.Remove(c.NodeId);
                        addedById.Remove(c.NodeId);
                        deleted.Add(c.NodeId);
                    }
                    else
                    {
                        // theirs edits — undo mine's deletion, take theirs' node
                        deleted.Remove(c.NodeId);
                        if (theirModById.TryGetValue(c.NodeId, out var tm))
                            modifiedById[c.NodeId] = tm;
                        if (theirAddedById.TryGetValue(c.NodeId, out var ta))
                            addedById[c.NodeId] = ta;
                    }
                    break;

                case MergeConflictKind.NodeAddAdd:
                    addedById[c.NodeId] = theirAddedById[c.NodeId];
                    break;

                case MergeConflictKind.ConversationLevel:
                    return theirPatch; // whole-conversation replacement
            }
        }

        return minePatch with
        {
            AddedNodes     = addedById.Values.ToList(),
            DeletedNodeIds = deleted.ToList(),
            ModifiedNodes  = modifiedById.Values.ToList(),
            Translations   = translations.ToDictionary(
                kv => kv.Key, kv => (IReadOnlyList<NodeTranslation>)kv.Value),
        };
    }

    // Replace (or add) the translation for a node within a language list.
    private static void SetTranslation(
        Dictionary<string, List<NodeTranslation>> translations, string lang, NodeTranslation t)
    {
        if (!translations.TryGetValue(lang, out var list))
            translations[lang] = list = [];
        var idx = list.FindIndex(x => x.NodeId == t.NodeId);
        if (idx >= 0) list[idx] = t;
        else          list.Add(t);
    }

    // Index by id with last-wins semantics — tolerant of a malformed reconstruction
    // that yields duplicate NodeIds (a plain ToDictionary would throw).
    private static Dictionary<int, T> ById<T>(IEnumerable<T> items, Func<T, int> key)
    {
        var map = new Dictionary<int, T>();
        foreach (var item in items)
            map[key(item)] = item;
        return map;
    }

    // Returns a copy of `mineMod` with the named field taken from `theirMod`.
    private static NodeModification ApplyField(
        NodeModification mineMod, NodeModification theirMod, string field)
    {
        if (field == "Conditions")
            return new NodeModification(
                mineMod.NodeId, mineMod.FieldChanges,
                mineMod.AddedLinks, mineMod.DeletedLinks, mineMod.ModifiedLinks)
            {
                UpdatedConditions = theirMod.UpdatedConditions,
                UpdatedScripts    = mineMod.UpdatedScripts,
            };

        if (field == "Scripts")
            return new NodeModification(
                mineMod.NodeId, mineMod.FieldChanges,
                mineMod.AddedLinks, mineMod.DeletedLinks, mineMod.ModifiedLinks)
            {
                UpdatedConditions = mineMod.UpdatedConditions,
                UpdatedScripts    = theirMod.UpdatedScripts,
            };

        var fields = new Dictionary<string, FieldChange>(mineMod.FieldChanges)
        {
            [field] = theirMod.FieldChanges[field],
        };
        return new NodeModification(
            mineMod.NodeId, fields,
            mineMod.AddedLinks, mineMod.DeletedLinks, mineMod.ModifiedLinks)
        {
            UpdatedConditions = mineMod.UpdatedConditions,
            UpdatedScripts    = mineMod.UpdatedScripts,
        };
    }
}
