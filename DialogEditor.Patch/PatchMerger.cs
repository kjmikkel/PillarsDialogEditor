using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

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

        // Translations (GitHub issue 100): per language, per NodeId, later wins — the same rule
        // as fields. Since schema 2 this is where all node text lives, so leaving it out
        // silently dropped every line of text of every mod sharing this conversation.
        // Entries for deleted nodes are dropped with the node, as TranslationApplier has
        // nothing left to write them to.
        var deletedSet = deleted.ToHashSet();
        var translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>();
        foreach (var language in patches.SelectMany(p => p.Translations.Keys).Distinct())
        {
            var byNode = new Dictionary<int, NodeTranslation>();
            foreach (var patch in patches)
                if (patch.Translations.TryGetValue(language, out var entries))
                    foreach (var entry in entries)
                        byNode[entry.NodeId] = entry;

            var kept = byNode.Values.Where(t => !deletedSet.Contains(t.NodeId)).ToList();
            if (kept.Count > 0)
                translations[language] = kept;
        }

        // NodeComments: translator context, same last-wins rule per NodeId.
        var comments = new Dictionary<int, string>();
        foreach (var patch in patches)
            foreach (var (nodeId, comment) in patch.NodeComments)
                if (!deletedSet.Contains(nodeId))
                    comments[nodeId] = comment;

        return new ConversationPatch(
            conversationName,
            ConversationPatch.CurrentSchemaVersion,
            addedById.Values.ToList(),
            deleted,
            modifiedById.Values.ToList())
        {
            Translations = translations,
            NodeComments = comments,
        };
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
