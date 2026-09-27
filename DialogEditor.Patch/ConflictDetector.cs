namespace DialogEditor.Patch;

/// One conflict between two patches in the load order.
///
/// FieldName is null for a delete-vs-modify conflict (see IsDeletion), where there is
/// no single field to name. It used to hold the literal "(deleted)" instead — but this
/// record is also the identity of a conflict (ConflictDetector de-duplicates on
/// FieldName among other things) as well as the text the Patch Manager shows, so that
/// one string could not be translated without making de-duplication depend on the UI
/// language. DialogEditor.Patch references only Core and cannot reach Loc in any case.
/// The kind is reported as data and PatchConflictRowViewModel renders the label, the
/// same split as MergeConflict.DeletedSide for git merges.
///
/// Kind (issue #6) distinguishes the clashes that have no field name: a deletion, two
/// packs adding a node under the same id, and two packs editing the same link. It
/// defaults from FieldName so the original field / delete-vs-modify construction is
/// unchanged; the new kinds set it explicitly.
public record PatchConflict(
    string  ConversationName,
    int     NodeId,
    string? FieldName,
    int     FirstPatchIndex,
    int     SecondPatchIndex)
{
    public PatchConflictKind Kind { get; init; } =
        FieldName is null ? PatchConflictKind.Deletion : PatchConflictKind.Field;

    /// For a Link conflict, the target of the contested NodeId → LinkToNodeId link.
    public int? LinkToNodeId { get; init; }

    /// For a Text conflict, the language code both packs wrote the node's text in.
    public string? Language { get; init; }

    /// True when one patch deletes the node that another patch modifies.
    public bool IsDeletion => Kind == PatchConflictKind.Deletion;
}

public enum PatchConflictKind
{
    /// Two patches change the same field of the same node.
    Field,
    /// One patch deletes a node another patch modifies.
    Deletion,
    /// Two patches add a node under the same id. PatchMerger keeps only the later node,
    /// so the earlier pack's node is replaced wholesale — and its own links then lead to
    /// a node it never wrote. Likely whenever two mods extend the same base conversation,
    /// because both allocate new ids from the same max+1.
    AddedNode,
    /// Two patches add or modify the same NodeId → LinkToNodeId link (PatchMerger keys
    /// added and modified links by target alike, later wins).
    Link,
    /// Two patches write the same node's text in the same language (GitHub issue 100). Text
    /// lives only in ConversationPatch.Translations since schema 2, so it never shows up
    /// as a field change; PatchMerger resolves it last-wins per language and node.
    /// Different languages do not clash: a translation pack over a content mod is the
    /// point of having languages.
    Text,
}

public static class ConflictDetector
{
    /// Detects conflicts across an ordered list of projects.
    /// A conflict exists when two projects both modify the same (conversation, nodeId, field).
    /// Also flags delete-vs-modify conflicts (one project deletes a node another modifies).
    public static IReadOnlyList<PatchConflict> Detect(
        IReadOnlyList<(string ProjectName, IReadOnlyDictionary<string, ConversationPatch> Patches)> projects)
    {
        var conflicts = new List<PatchConflict>();

        // Collect field changes per conversation: key = (nodeId, fieldName)
        // value = list of project indices that modify it
        var fieldTouches = new Dictionary<string,                        // conversationName
            Dictionary<(int nodeId, string field), List<int>>>();        // → projectIdx list

        // Collect deletions per conversation: key = nodeId, value = project indices
        var deletions = new Dictionary<string, Dictionary<int, List<int>>>();

        // Added node ids and added/modified links, same shape (issue #6).
        var additions = new Dictionary<string, Dictionary<int, List<int>>>();
        var linkTouches = new Dictionary<string, Dictionary<(int from, int to), List<int>>>();
        // Node text per language (GitHub issue 100) — it lives in Translations, not FieldChanges.
        var textTouches = new Dictionary<string, Dictionary<(int nodeId, string lang), List<int>>>();

        for (int pi = 0; pi < projects.Count; pi++)
        {
            var (_, patches) = projects[pi];

            foreach (var (convName, patch) in patches)
            {
                // Track field modifications
                if (!fieldTouches.TryGetValue(convName, out var convFields))
                    fieldTouches[convName] = convFields = [];

                foreach (var node in patch.AddedNodes)
                    Touch(additions, convName, node.NodeId, pi);

                // A pack's text for a node it adds itself is part of that node: if two
                // packs add the same id, the AddedNode conflict already covers it.
                var ownNodes = patch.AddedNodes.Select(n => n.NodeId).ToHashSet();
                foreach (var (lang, entries) in patch.Translations)
                    foreach (var nodeId in entries.Select(t => t.NodeId).Distinct())
                        if (!ownNodes.Contains(nodeId))
                            Touch(textTouches, convName, (nodeId, lang), pi);

                foreach (var mod in patch.ModifiedNodes)
                {
                    // One touch per link per project, even if a patch both adds and
                    // modifies it, so a single pack never conflicts with itself.
                    foreach (var to in mod.AddedLinks.Select(l => l.ToNodeId)
                                         .Concat(mod.ModifiedLinks.Select(l => l.ToNodeId))
                                         .Distinct())
                        Touch(linkTouches, convName, (mod.NodeId, to), pi);

                    foreach (var fieldName in mod.FieldChanges.Keys)
                    {
                        var key = (mod.NodeId, fieldName);
                        if (!convFields.TryGetValue(key, out var list))
                            convFields[key] = list = [];
                        list.Add(pi);
                    }
                    // Conditions and Scripts are replace-all — treat as a field
                    if (mod.UpdatedConditions is not null)
                    {
                        var key = (mod.NodeId, "Conditions");
                        if (!convFields.TryGetValue(key, out var list))
                            convFields[key] = list = [];
                        list.Add(pi);
                    }
                    if (mod.UpdatedScripts is not null)
                    {
                        var key = (mod.NodeId, "Scripts");
                        if (!convFields.TryGetValue(key, out var list))
                            convFields[key] = list = [];
                        list.Add(pi);
                    }
                }

                // Track deletions
                if (!deletions.TryGetValue(convName, out var convDels))
                    deletions[convName] = convDels = [];

                foreach (var nodeId in patch.DeletedNodeIds)
                {
                    if (!convDels.TryGetValue(nodeId, out var dlist))
                        convDels[nodeId] = dlist = [];
                    dlist.Add(pi);
                }
            }
        }

        // Field conflicts: any field touched by more than one project
        foreach (var (convName, fields) in fieldTouches)
        {
            foreach (var ((nodeId, fieldName), indices) in fields)
            {
                if (indices.Count >= 2)
                    conflicts.Add(new PatchConflict(convName, nodeId, fieldName,
                        indices[0], indices[1]));
            }
        }

        // Delete-vs-modify conflicts
        foreach (var (convName, convDels) in deletions)
        {
            if (!fieldTouches.TryGetValue(convName, out var convFields)) continue;

            foreach (var (nodeId, delIndices) in convDels)
            {
                // Check if any project modifies this node while another deletes it
                var modifyingProjects = convFields.Keys
                    .Where(k => k.nodeId == nodeId)
                    .SelectMany(k => convFields[k])
                    .Concat(textTouches.GetValueOrDefault(convName)?
                                .Where(t => t.Key.nodeId == nodeId)
                                .SelectMany(t => t.Value) ?? [])
                    .Distinct()
                    .ToList();

                foreach (var modIdx in modifyingProjects)
                {
                    foreach (var delIdx in delIndices)
                    {
                        if (modIdx != delIdx)
                            conflicts.Add(new PatchConflict(
                                convName, nodeId, null, delIdx, modIdx));
                    }
                }
            }
        }

        // Added-node and link conflicts: the same id / link from more than one project
        foreach (var (convName, nodes) in additions)
            foreach (var (nodeId, indices) in nodes)
                if (indices.Count >= 2)
                    conflicts.Add(new PatchConflict(convName, nodeId, null, indices[0], indices[1])
                        { Kind = PatchConflictKind.AddedNode });

        foreach (var (convName, links) in linkTouches)
            foreach (var ((from, to), indices) in links)
                if (indices.Count >= 2)
                    conflicts.Add(new PatchConflict(convName, from, null, indices[0], indices[1])
                        { Kind = PatchConflictKind.Link, LinkToNodeId = to });

        foreach (var (convName, texts) in textTouches)
            foreach (var ((nodeId, lang), indices) in texts)
                if (indices.Count >= 2)
                    conflicts.Add(new PatchConflict(convName, nodeId, null, indices[0], indices[1])
                        { Kind = PatchConflictKind.Text, Language = lang });

        return conflicts
            .DistinctBy(c => (c.ConversationName, c.NodeId, c.FieldName, c.Kind, c.LinkToNodeId,
                              c.Language, c.FirstPatchIndex, c.SecondPatchIndex))
            .ToList();
    }

    private static void Touch<TKey>(Dictionary<string, Dictionary<TKey, List<int>>> touches,
                                    string convName, TKey key, int projectIndex) where TKey : notnull
    {
        if (!touches.TryGetValue(convName, out var byKey))
            touches[convName] = byKey = [];
        if (!byKey.TryGetValue(key, out var list))
            byKey[key] = list = [];
        list.Add(projectIndex);
    }
}
