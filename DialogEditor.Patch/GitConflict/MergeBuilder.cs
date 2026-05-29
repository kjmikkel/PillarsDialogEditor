namespace DialogEditor.Patch.GitConflict;

/// Produces the merged project from the user's per-conflict resolutions.
///
/// Because the reconstructed sides are identical outside git's conflict hunks,
/// the merge starts from the "mine" project and overlays only the values for
/// conflicts the user resolved to "theirs". Conversations present in only one
/// side are carried through unchanged.
public static class MergeBuilder
{
    private const string DeletedMarker = "(deleted)";

    public static DialogProject Build(
        DialogProject mine,
        DialogProject theirs,
        IReadOnlyDictionary<MergeConflict, MergeSide> choices)
    {
        var patches = new Dictionary<string, ConversationPatch>(mine.Patches);

        // Carry through conversations that exist only on the "theirs" side.
        foreach (var (conv, theirPatch) in theirs.Patches)
            if (!patches.ContainsKey(conv))
                patches[conv] = theirPatch;

        // Apply theirs-resolved conflicts, grouped per conversation.
        var theirsResolved = choices
            .Where(kv => kv.Value == MergeSide.Theirs)
            .Select(kv => kv.Key)
            .GroupBy(c => c.ConversationName);

        foreach (var group in theirsResolved)
        {
            var conv = group.Key;
            patches[conv] = ApplyToPatch(mine.Patches[conv], theirs.Patches[conv], group);
        }

        return mine with { Patches = patches };
    }

    private static ConversationPatch ApplyToPatch(
        ConversationPatch minePatch,
        ConversationPatch theirPatch,
        IEnumerable<MergeConflict> theirsConflicts)
    {
        var modifiedById = minePatch.ModifiedNodes.ToDictionary(m => m.NodeId);
        var addedById    = minePatch.AddedNodes.ToDictionary(n => n.NodeId);
        var deleted      = minePatch.DeletedNodeIds.ToHashSet();

        var theirModById   = theirPatch.ModifiedNodes.ToDictionary(m => m.NodeId);
        var theirAddedById = theirPatch.AddedNodes.ToDictionary(n => n.NodeId);

        foreach (var c in theirsConflicts)
        {
            switch (c.Kind)
            {
                case MergeConflictKind.FieldEdit:
                    modifiedById[c.NodeId] = ApplyField(
                        modifiedById[c.NodeId], theirModById[c.NodeId], c.FieldName!);
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
        };
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
