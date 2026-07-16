using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Analytics;

/// <summary>
/// Playthrough-oriented stats over one conversation's graph. Pure and IO-free.
/// Cycles are broken to a DAG (back-edges to a DFS ancestor are dropped), so longest/
/// shortest playthroughs are well-defined and O(V+E). Every metric is computed under two
/// per-node weight functions — Default text words, and Female text words (falling back to
/// Default where a node has no female text) — with a 10% total-difference significance gate.
///
/// Conventions shared with FlowAnalysisService: root is node 0; reachability is from root.
/// The overall longest/shortest include the root line; each branch's content/longest are
/// measured from the choice onward (the root is shared, so it's excluded for comparison).
/// Spec: docs/superpowers/specs/2026-07-13-path-based-writing-stats-design.md
/// </summary>
public static class PathStatsService
{
    private const double FemaleSignificanceThreshold = 0.10;

    public static PathStatsReport Analyze(ConversationEditSnapshot snapshot)
    {
        var nodes = snapshot.Nodes;
        if (nodes.Count == 0)
            return new PathStatsReport(false, 0, 0, 0, 0, 0, 0, [], []);

        var nodeById = nodes.ToDictionary(n => n.NodeId);

        static int Words(string? t) =>
            string.IsNullOrEmpty(t) ? 0 : t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int Def(NodeEditSnapshot n) => Words(n.DefaultText);
        int Fem(NodeEditSnapshot n) =>
            string.IsNullOrWhiteSpace(n.FemaleText) ? Words(n.DefaultText) : Words(n.FemaleText);

        // Totals + significance over ALL nodes (structure-independent).
        var defaultTotal = nodes.Sum(Def);
        var femaleTotal  = nodes.Sum(Fem);
        var significant  = defaultTotal > 0 &&
            Math.Abs(femaleTotal - defaultTotal) / (double)defaultTotal > FemaleSignificanceThreshold;

        var wordsPerSpeaker = nodes
            .GroupBy(n => n.SpeakerGuid)
            .Select(g => new SpeakerWordCount(g.Key, g.First().SpeakerCategory, g.Sum(Def), g.Sum(Fem)))
            .OrderByDescending(s => s.DefaultWords)
            .ToList();

        if (!nodeById.ContainsKey(0))
            return new PathStatsReport(significant, defaultTotal, femaleTotal, 0, 0, 0, 0,
                wordsPerSpeaker, []);

        // ── Break to a DAG (drop back-edges to a DFS ancestor) ────────────
        var dag     = new Dictionary<int, List<int>>();
        var onStack = new HashSet<int>();
        var visited = new HashSet<int>();
        void Dfs(int u)
        {
            visited.Add(u);
            onStack.Add(u);
            dag[u] = [];
            foreach (var link in nodeById[u].Links)
            {
                var v = link.ToNodeId;
                if (!nodeById.ContainsKey(v)) continue;   // dangling
                if (onStack.Contains(v)) continue;         // back-edge → drop
                dag[u].Add(v);
                if (!visited.Contains(v)) Dfs(v);
            }
            onStack.Remove(u);
        }
        Dfs(0);

        // Memoised longest/shortest weighted path on the DAG (one memo per weight fn).
        var longMemo  = new Dictionary<(int, bool), int>();
        var shortMemo = new Dictionary<(int, bool), int>();

        int Longest(int u, bool female)
        {
            if (longMemo.TryGetValue((u, female), out var cached)) return cached;
            var w = female ? Fem(nodeById[u]) : Def(nodeById[u]);
            var best = w;
            if (dag.TryGetValue(u, out var outs) && outs.Count > 0)
                best = w + outs.Max(v => Longest(v, female));
            longMemo[(u, female)] = best;
            return best;
        }
        int Shortest(int u, bool female)
        {
            if (shortMemo.TryGetValue((u, female), out var cached)) return cached;
            var w = female ? Fem(nodeById[u]) : Def(nodeById[u]);
            var best = w;
            if (dag.TryGetValue(u, out var outs) && outs.Count > 0)
                best = w + outs.Min(v => Shortest(v, female));
            shortMemo[(u, female)] = best;
            return best;
        }

        // Reachable-set content sum on the FULL graph (cycle-safe via visited set).
        int ReachableSum(int start, bool female)
        {
            var seen  = new HashSet<int> { start };
            var queue = new Queue<int>();
            queue.Enqueue(start);
            var sum = 0;
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                sum += female ? Fem(nodeById[u]) : Def(nodeById[u]);
                foreach (var link in nodeById[u].Links)
                    if (nodeById.ContainsKey(link.ToNodeId) && seen.Add(link.ToNodeId))
                        queue.Enqueue(link.ToNodeId);
            }
            return sum;
        }

        // Opening choices: root's direct link targets that are player choices.
        var branches = new List<BranchStat>();
        foreach (var link in nodeById[0].Links)
        {
            if (!nodeById.TryGetValue(link.ToNodeId, out var c) || !c.IsPlayerChoice) continue;
            branches.Add(new BranchStat(
                c.NodeId, c.DefaultText ?? "",
                ReachableSum(c.NodeId, female: false), Longest(c.NodeId, female: false),
                ReachableSum(c.NodeId, female: true),  Longest(c.NodeId, female: true)));
        }
        branches = branches.OrderBy(b => b.ChoiceNodeId).ToList();

        return new PathStatsReport(
            significant, defaultTotal, femaleTotal,
            Longest(0, false),  Shortest(0, false),
            Longest(0, true),   Shortest(0, true),
            wordsPerSpeaker, branches);
    }
}
