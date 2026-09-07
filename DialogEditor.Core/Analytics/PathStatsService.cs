using DialogEditor.Core.Editing;

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

    /// How many levels of player choice the fork breakdown descends (issue #14).
    ///
    /// The fork tree is already bounded by the loop guard, but a long enough chain of
    /// choices still nests as deep as the conversation is written, and nobody reads a
    /// forty-deep indent. This caps what the report carries, not what the graph contains.
    public const int MaxForkDepth = 10;

    public static PathStatsReport Analyze(ConversationEditSnapshot snapshot)
    {
        var nodes = snapshot.Nodes;
        if (nodes.Count == 0)
            return new PathStatsReport(false, 0, 0, 0, 0, 0, 0, [], [], []);

        var nodeById = nodes.ToDictionary(n => n.NodeId);

        static int Words(string? t) =>
            string.IsNullOrEmpty(t) ? 0 : t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int Def(NodeEditSnapshot n) => Words(n.DefaultText);
        int Fem(NodeEditSnapshot n) =>
            string.IsNullOrWhiteSpace(n.FemaleText) ? Words(n.DefaultText) : Words(n.FemaleText);
        int Weight(int id, bool female) => female ? Fem(nodeById[id]) : Def(nodeById[id]);

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
                wordsPerSpeaker, [], []);

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
            var best = Weight(u, female);
            if (dag.TryGetValue(u, out var outs) && outs.Count > 0)
                best += outs.Max(v => Longest(v, female));
            longMemo[(u, female)] = best;
            return best;
        }
        int Shortest(int u, bool female)
        {
            if (shortMemo.TryGetValue((u, female), out var cached)) return cached;
            var best = Weight(u, female);
            if (dag.TryGetValue(u, out var outs) && outs.Count > 0)
                best += outs.Min(v => Shortest(v, female));
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
                sum += Weight(u, female);
                foreach (var link in nodeById[u].Links)
                    if (nodeById.ContainsKey(link.ToNodeId) && seen.Add(link.ToNodeId))
                        queue.Enqueue(link.ToNodeId);
            }
            return sum;
        }

        // ── Fork tree (issue #14) ─────────────────────────────────────────
        // A fork is where the player decides. From a starting node, walk forward until
        // player-choice nodes are met and stop there — those are the fork. v1 took the
        // root's DIRECT links instead, so a conversation opening with an NPC greeting
        // before the choice menu reported no branches at all.
        //
        // The walk uses the FULL graph, not the DAG: a choice reachable only by looping
        // back to a hub is still a choice the player is offered.
        List<int> ChoiceFrontier(int start)
        {
            var seen  = new HashSet<int> { start };
            var queue = new Queue<int>();
            var found = new List<int>();
            void Enqueue(int u)
            {
                foreach (var link in nodeById[u].Links)
                    if (nodeById.ContainsKey(link.ToNodeId) && seen.Add(link.ToNodeId))
                        queue.Enqueue(link.ToNodeId);
            }
            Enqueue(start);
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                if (nodeById[u].IsPlayerChoice) found.Add(u);   // fork boundary: stop here
                else Enqueue(u);
            }
            return found;
        }

        // The choices on the way to here. A frontier choice already on this stack is
        // dropped rather than recursed into — the same "a loop counts once" rule as the
        // DAG cut, and what stops a hub-and-spoke menu from unrolling forever.
        var forkStack = new HashSet<int>();
        List<BranchStat> Forks(int start, int depth)
        {
            if (depth > MaxForkDepth) return [];
            var result = new List<BranchStat>();
            foreach (var c in ChoiceFrontier(start).Where(c => !forkStack.Contains(c)).OrderBy(c => c))
            {
                forkStack.Add(c);
                var subs = Forks(c, depth + 1);
                forkStack.Remove(c);

                result.Add(new BranchStat(
                    c, nodeById[c].DefaultText ?? "",
                    ReachableSum(c, female: false), Longest(c, female: false),
                    ReachableSum(c, female: true),  Longest(c, female: true),
                    subs));
            }
            return result;
        }
        var branches = Forks(0, 1);

        // ── Endings (issue #14) ───────────────────────────────────────────
        // Root-to-ending figures are the mirror of Longest/Shortest: walk the DAG's edges
        // backwards. Node 0 has no DAG predecessors by construction — it is on the DFS
        // stack for the whole traversal, so every edge into it is a dropped back-edge —
        // which makes the recursion well-founded without a separate base case.
        var preds = new Dictionary<int, List<int>>();
        foreach (var (u, outs) in dag)
            foreach (var v in outs)
            {
                if (!preds.TryGetValue(v, out var list)) preds[v] = list = [];
                list.Add(u);
            }

        var longToMemo  = new Dictionary<(int, bool), int>();
        var shortToMemo = new Dictionary<(int, bool), int>();
        int LongestTo(int u, bool female)
        {
            if (longToMemo.TryGetValue((u, female), out var cached)) return cached;
            var best = Weight(u, female);
            if (preds.TryGetValue(u, out var ins) && ins.Count > 0)
                best += ins.Max(p => LongestTo(p, female));
            longToMemo[(u, female)] = best;
            return best;
        }
        int ShortestTo(int u, bool female)
        {
            if (shortToMemo.TryGetValue((u, female), out var cached)) return cached;
            var best = Weight(u, female);
            if (preds.TryGetValue(u, out var ins) && ins.Count > 0)
                best += ins.Min(p => ShortestTo(p, female));
            shortToMemo[(u, female)] = best;
            return best;
        }

        var endings = dag.Keys
            .Where(id => nodeById[id].Links.Count == 0)     // a real dead end, not a loop-back
            .Select(id => new EndingStat(
                id, nodeById[id].DefaultText ?? "",
                LongestTo(id, female: false), ShortestTo(id, female: false),
                LongestTo(id, female: true),  ShortestTo(id, female: true)))
            .OrderByDescending(e => e.DefaultLongestWords)
            .ThenBy(e => e.NodeId)
            .ToList();

        return new PathStatsReport(
            significant, defaultTotal, femaleTotal,
            Longest(0, false),  Shortest(0, false),
            Longest(0, true),   Shortest(0, true),
            wordsPerSpeaker, branches, endings);
    }
}
