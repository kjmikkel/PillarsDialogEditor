namespace DialogEditor.Patch.Diff;

/// Best-effort, patch-level dangling-link detection. Flags any link — from an
/// added node's Links, or a modified node's AddedLinks/ModifiedLinks — whose
/// target node is deleted by the same conversation's patch. Does not reconstruct
/// against base game data (full reachability is deferred); pairs with the
/// "warn, but allow" stance.
public static class NodeLinkAnalyzer
{
    public static IReadOnlyList<DanglingLink> Analyze(DialogProject projected)
    {
        var result = new List<DanglingLink>();

        foreach (var (conv, patch) in projected.Patches)
        {
            var deleted = patch.DeletedNodeIds.ToHashSet();
            if (deleted.Count == 0) continue;

            foreach (var n in patch.AddedNodes)
                foreach (var link in n.Links)
                    if (deleted.Contains(link.ToNodeId))
                        result.Add(new DanglingLink(conv, n.NodeId, link.ToNodeId));

            foreach (var m in patch.ModifiedNodes)
            {
                foreach (var link in m.AddedLinks)
                    if (deleted.Contains(link.ToNodeId))
                        result.Add(new DanglingLink(conv, m.NodeId, link.ToNodeId));
                foreach (var link in m.ModifiedLinks)
                    if (deleted.Contains(link.ToNodeId))
                        result.Add(new DanglingLink(conv, m.NodeId, link.ToNodeId));
            }
        }

        return result;
    }
}
