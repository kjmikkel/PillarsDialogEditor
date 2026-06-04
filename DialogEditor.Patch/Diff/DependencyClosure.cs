namespace DialogEditor.Patch.Diff;

/// Transitive closure of a node's outgoing link targets, restricted to nodes that
/// are *added* (would not otherwise exist after a selective apply). Used to pull a
/// brought-in node's added link targets so its links don't point at nodes that were
/// never created. Cycle-safe; the start node is never part of the result.
public static class DependencyClosure
{
    public static IReadOnlySet<int> Expand(
        int start,
        IReadOnlyDictionary<int, IReadOnlyList<int>> outgoing,
        IReadOnlySet<int> addedIds)
    {
        var result  = new HashSet<int>();
        var visited = new HashSet<int> { start };
        var stack   = new Stack<int>();
        stack.Push(start);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (!outgoing.TryGetValue(node, out var targets)) continue;

            foreach (var t in targets)
            {
                if (!addedIds.Contains(t)) continue; // only pull added targets
                if (!visited.Add(t)) continue;       // cycle / duplicate guard
                result.Add(t);
                stack.Push(t);                        // follow transitively
            }
        }

        return result;
    }
}
