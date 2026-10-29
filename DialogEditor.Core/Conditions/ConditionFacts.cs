using DialogEditor.Core.Models;

namespace DialogEditor.Core.Conditions;

public enum FactKind { Global, Companion, PlayedNode, Quest, InCombat }

/// One piece of game state a condition reads. Key is the global name, companion GUID,
/// conversation key or quest id (empty for InCombat); NodeId is used by PlayedNode only.
public sealed record ConditionFact(FactKind Kind, string Key, int NodeId = 0);

public static class ConditionFacts
{
    /// The distinct facts the supported calls in <paramref name="nodes"/> read, in first-seen
    /// order, recursing into branches. Global names compare case-sensitively (as the game does);
    /// the other keys compare case-insensitively.
    public static IReadOnlyList<ConditionFact> Collect(IEnumerable<ConditionNode> nodes)
    {
        var seen = new HashSet<(FactKind, string, int)>(new KeyComparer());
        var result = new List<ConditionFact>();
        foreach (var node in nodes)
            foreach (var leaf in node.Leaves().OfType<ConditionLeaf>())
                foreach (var fact in SupportedConditions.FactsOf(leaf))
                    if (seen.Add((fact.Kind, fact.Key, fact.NodeId)))
                        result.Add(fact);
        return result;
    }

    private sealed class KeyComparer : IEqualityComparer<(FactKind Kind, string Key, int NodeId)>
    {
        public bool Equals((FactKind Kind, string Key, int NodeId) a, (FactKind Kind, string Key, int NodeId) b)
            => a.Kind == b.Kind && a.NodeId == b.NodeId
               && string.Equals(a.Key, b.Key, a.Kind == FactKind.Global
                   ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((FactKind Kind, string Key, int NodeId) k)
            => HashCode.Combine(k.Kind, k.NodeId, StringComparer.OrdinalIgnoreCase.GetHashCode(k.Key));
    }
}
