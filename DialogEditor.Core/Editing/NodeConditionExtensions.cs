using DialogEditor.Core.Models;

namespace DialogEditor.Core.Editing;

/// Shared walk over a node's condition match-sites: its own condition tree and
/// each outgoing link's condition tree, flattened to leaves. Used by the
/// reputation/disposition tally (conditions only) and by the node-search feature.
public static class NodeConditionExtensions
{
    public static IEnumerable<ConditionLeaf> ConditionLeaves(this NodeEditSnapshot node)
    {
        foreach (var leaf in node.Conditions.SelectMany(c => c.Leaves()).OfType<ConditionLeaf>())
            yield return leaf;

        foreach (var link in node.Links)
            if (link.Conditions is { } conds)
                foreach (var leaf in conds.SelectMany(c => c.Leaves()).OfType<ConditionLeaf>())
                    yield return leaf;
    }
}
