using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Search;

/// Finds the nodes in one conversation whose conditions or scripts match a CatalogueMatch
/// query. A node is a hit if the query matches any leaf in its own condition tree, any leaf
/// in any outgoing link's condition tree (both via ConditionLeaves), or any of its scripts.
/// Node-only granularity: a node matching in multiple sites appears once. Pure; no IO.
public static class NodeConditionSearchService
{
    public static IReadOnlySet<int> FindMatches(ConversationEditSnapshot snapshot, CatalogueMatch query)
    {
        var hits = new HashSet<int>();
        foreach (var node in snapshot.Nodes)
        {
            if (node.ConditionLeaves().Any(query.Matches) || node.Scripts.Any(query.Matches))
                hits.Add(node.NodeId);
        }
        return hits;
    }
}
