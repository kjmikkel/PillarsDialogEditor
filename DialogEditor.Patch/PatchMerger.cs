using DialogEditor.Core.Editing;

namespace DialogEditor.Patch;

public static class PatchMerger
{
    /// Merges patches for one conversation from multiple projects into a single
    /// effective patch. Later projects win on any contested field (last-wins semantics).
    public static ConversationPatch Merge(
        string conversationName,
        IReadOnlyList<ConversationPatch> patches)
    {
        if (patches.Count == 0)
            return new ConversationPatch(conversationName, ConversationPatch.CurrentSchemaVersion, [], [], []);
        if (patches.Count == 1)
            return patches[0];

        // AddedNodes: later definitions replace earlier for the same NodeId
        var addedById = new Dictionary<int, NodeEditSnapshot>();
        foreach (var patch in patches)
            foreach (var node in patch.AddedNodes)
                addedById[node.NodeId] = node;

        // DeletedNodeIds: union
        var deleted = patches
            .SelectMany(p => p.DeletedNodeIds)
            .Distinct()
            .ToList();

        // ModifiedNodes: merge per NodeId — later patches win field-by-field
        var modifiedById = new Dictionary<int, NodeModification>();
        foreach (var patch in patches)
        {
            foreach (var mod in patch.ModifiedNodes)
            {
                if (!modifiedById.TryGetValue(mod.NodeId, out var existing))
                {
                    modifiedById[mod.NodeId] = mod;
                }
                else
                {
                    modifiedById[mod.NodeId] = MergeModifications(existing, mod);
                }
            }
        }

        return new ConversationPatch(
            conversationName,
            ConversationPatch.CurrentSchemaVersion,
            addedById.Values.ToList(),
            deleted,
            modifiedById.Values.ToList());
    }

    private static NodeModification MergeModifications(NodeModification earlier, NodeModification later)
    {
        // Field changes: later wins per field name
        var mergedFields = new Dictionary<string, FieldChange>(earlier.FieldChanges);
        foreach (var (k, v) in later.FieldChanges)
            mergedFields[k] = v;

        // AddedLinks: union by ToNodeId, later wins
        var addedLinksById = new Dictionary<int, LinkEditSnapshot>();
        foreach (var l in earlier.AddedLinks)  addedLinksById[l.ToNodeId] = l;
        foreach (var l in later.AddedLinks)    addedLinksById[l.ToNodeId] = l;

        // DeletedLinks: union
        var deletedLinks = earlier.DeletedLinks
            .Concat(later.DeletedLinks)
            .DistinctBy(l => l.ToNodeId)
            .ToList();

        // ModifiedLinks: union by ToNodeId, later wins
        var modLinksById = new Dictionary<int, ModifiedLink>();
        foreach (var l in earlier.ModifiedLinks) modLinksById[l.ToNodeId] = l;
        foreach (var l in later.ModifiedLinks)   modLinksById[l.ToNodeId] = l;

        return new NodeModification(
            earlier.NodeId,
            mergedFields,
            addedLinksById.Values.ToList(),
            deletedLinks,
            modLinksById.Values.ToList())
        {
            // Later wins for replace-all fields
            UpdatedConditions = later.UpdatedConditions ?? earlier.UpdatedConditions,
            UpdatedScripts    = later.UpdatedScripts    ?? earlier.UpdatedScripts,
        };
    }
}
