using DialogEditor.Core.Models;

namespace DialogEditor.Core.Conditions;

/// <summary>
/// Decides whether a condition list passes against an assumed <see cref="GameState"/>, using the
/// game's own fold. Leaves the state cannot settle are enumerated both ways; the verdict is
/// True/False only if every assignment agrees, otherwise Unknown.
/// </summary>
public static class ConditionEvaluator
{
    private const int MaxUnknowns = 12;

    public static Truth Evaluate(IReadOnlyList<ConditionNode> list, GameState state, string gameId)
        => Evaluate(list, state, gameId, DefaultLeaf);

    // Replaced by SupportedConditions.Evaluate in Task 2; until then nothing is supported.
    private static Truth DefaultLeaf(ConditionLeaf leaf, GameState state) => Truth.Unknown;

    /// Test seam: lets tests inject the per-leaf evaluator.
    internal static Truth Evaluate(
        IReadOnlyList<ConditionNode> list, GameState state, string gameId,
        Func<ConditionLeaf, GameState, Truth> leafEvaluator)
    {
        if (list.Count == 0) return Truth.True;
        if (!OperatorsValid(list)) return Truth.Unknown;

        var cache = new Dictionary<string, Truth>(StringComparer.Ordinal);
        var unknowns = new Dictionary<string, int>(StringComparer.Ordinal);
        Collect(list, state, leafEvaluator, cache, unknowns);
        if (unknowns.Count > MaxUnknowns) return Truth.Unknown;

        Func<IReadOnlyList<(bool Result, bool NextIsAnd)>, bool> fold =
            string.Equals(gameId, "poe1", StringComparison.OrdinalIgnoreCase)
                ? ConditionFold.Poe1
                : ConditionFold.Poe2;

        bool sawTrue = false, sawFalse = false;
        for (int mask = 0; mask < (1 << unknowns.Count); mask++)
        {
            if (Run(list, cache, unknowns, mask, fold)) sawTrue = true; else sawFalse = true;
            if (sawTrue && sawFalse) return Truth.Unknown;
        }
        return sawTrue ? Truth.True : Truth.False;
    }

    // Two occurrences with the same call and parameters are the same game fact.
    private static string Key(ConditionLeaf l) => l.FullName + "\u0001" + string.Join("\u0002", l.Parameters);

    private static bool IsAnd(string op) => string.Equals(op, "And", StringComparison.OrdinalIgnoreCase);
    private static bool IsOr(string op) => string.Equals(op, "Or", StringComparison.OrdinalIgnoreCase);

    // Only operators that link a component to a following one are used by the game.
    private static bool OperatorsValid(IReadOnlyList<ConditionNode> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            if (i < list.Count - 1 && !IsAnd(list[i].Operator) && !IsOr(list[i].Operator)) return false;
            if (list[i] is ConditionBranch b && !OperatorsValid(b.Components)) return false;
        }
        return true;
    }

    private static void Collect(
        IReadOnlyList<ConditionNode> list, GameState state,
        Func<ConditionLeaf, GameState, Truth> leafEvaluator,
        Dictionary<string, Truth> cache, Dictionary<string, int> unknowns)
    {
        foreach (var node in list)
        {
            if (node is ConditionBranch b)
            {
                Collect(b.Components, state, leafEvaluator, cache, unknowns);
                continue;
            }
            var leaf = (ConditionLeaf)node;
            var key = Key(leaf);
            if (cache.ContainsKey(key)) continue;
            var t = leafEvaluator(leaf, state);
            cache[key] = t;
            if (t == Truth.Unknown) unknowns[key] = unknowns.Count;
        }
    }

    private static bool Run(
        IReadOnlyList<ConditionNode> list, Dictionary<string, Truth> cache,
        Dictionary<string, int> unknowns, int mask,
        Func<IReadOnlyList<(bool Result, bool NextIsAnd)>, bool> fold)
    {
        var results = new List<(bool Result, bool NextIsAnd)>(list.Count);
        foreach (var node in list)
        {
            bool r;
            if (node is ConditionBranch b)
                r = Run(b.Components, cache, unknowns, mask, fold); // a branch's own Not is ignored, as in the game
            else
            {
                var leaf = (ConditionLeaf)node;
                var key = Key(leaf);
                var t = cache[key];
                r = t == Truth.Unknown ? ((mask >> unknowns[key]) & 1) == 1 : t == Truth.True;
                if (leaf.Not) r = !r;
            }
            results.Add((r, IsAnd(node.Operator)));
        }
        return fold(results);
    }
}
