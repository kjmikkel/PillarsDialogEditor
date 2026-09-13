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
public record PatchConflict(
    string  ConversationName,
    int     NodeId,
    string? FieldName,
    int     FirstPatchIndex,
    int     SecondPatchIndex)
{
    /// True when one patch deletes the node that another patch modifies.
    public bool IsDeletion => FieldName is null;
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

        for (int pi = 0; pi < projects.Count; pi++)
        {
            var (_, patches) = projects[pi];

            foreach (var (convName, patch) in patches)
            {
                // Track field modifications
                if (!fieldTouches.TryGetValue(convName, out var convFields))
                    fieldTouches[convName] = convFields = [];

                foreach (var mod in patch.ModifiedNodes)
                {
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

        return conflicts
            .DistinctBy(c => (c.ConversationName, c.NodeId, c.FieldName, c.FirstPatchIndex, c.SecondPatchIndex))
            .ToList();
    }
}
