using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Analytics;

/// <summary>
/// Playthrough-oriented stats over a conversation graph. Pure and IO-free.
///
/// The graph may span several conversations: nodes are keyed by <see cref="NodeRef"/>
/// (conversation + node id) and StartConversation handoffs are walked as a second kind of
/// edge (#14). A single conversation is the degenerate case — the snapshot overload wraps
/// into a one-conversation, no-jump graph, so both modes run this one implementation and
/// cannot drift. PathStatsServiceTests.SnapshotOverload_EqualsOneConversationGraph pins that.
///
/// Cycles are broken to a DAG (back-edges to a DFS ancestor are dropped), so longest/
/// shortest playthroughs are well-defined and O(V+E). Every metric is computed under two
/// per-node weight functions — Default text words, and Female text words (falling back to
/// Default where a node has no female text) — with a 10% total-difference significance gate.
///
/// Conventions shared with FlowAnalysisService: root is node 0 of the root conversation;
/// reachability is from root. The overall longest/shortest include the root line; each
/// branch's content/longest are measured from the choice onward (the root is shared, so it's
/// excluded for comparison).
///
/// Specs: docs/superpowers/specs/2026-07-13-path-based-writing-stats-design.md
///        docs/superpowers/specs/2026-09-10-cross-conversation-path-stats-design.md
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

    /// Single-conversation analysis. Wraps into a one-conversation, no-jump graph so there is
    /// exactly one implementation. The conversation name is the empty string: a snapshot
    /// carries no name, and inventing one would be a lie.
    public static PathStatsReport Analyze(ConversationEditSnapshot snapshot) =>
        Analyze(new MultiConversationGraph(
            RootConversation: "",
            Conversations: new Dictionary<string, ConversationEditSnapshot> { [""] = snapshot },
            Jumps: [],
            Unfollowed: []));

    public static PathStatsReport Analyze(MultiConversationGraph graph)
    {
        // Flatten every conversation's nodes into one NodeRef-keyed map.
        var nodeById = new Dictionary<NodeRef, NodeEditSnapshot>();
        foreach (var (conv, snap) in graph.Conversations)
            foreach (var n in snap.Nodes)
                nodeById[new NodeRef(conv, n.NodeId)] = n;

        var spanned = graph.Conversations.Count;

        if (nodeById.Count == 0)
            return new PathStatsReport(false, 0, 0, 0, 0, 0, 0, [], [], [],
                                       graph.Unfollowed, spanned);

        var allNodes = nodeById.Values.ToList();
        var root     = new NodeRef(graph.RootConversation, 0);

        static int Words(string? t) =>
            string.IsNullOrEmpty(t) ? 0 : t.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        int Def(NodeEditSnapshot n) => Words(n.DefaultText);
        int Fem(NodeEditSnapshot n) =>
            string.IsNullOrWhiteSpace(n.FemaleText) ? Words(n.DefaultText) : Words(n.FemaleText);
        int Weight(NodeRef id, bool female) => female ? Fem(nodeById[id]) : Def(nodeById[id]);

        // Totals + significance over ALL nodes (structure-independent).
        var defaultTotal = allNodes.Sum(Def);
        var femaleTotal  = allNodes.Sum(Fem);
        var significant  = defaultTotal > 0 &&
            Math.Abs(femaleTotal - defaultTotal) / (double)defaultTotal > FemaleSignificanceThreshold;

        var wordsPerSpeaker = allNodes
            .GroupBy(n => n.SpeakerGuid)
            .Select(g => new SpeakerWordCount(g.Key, g.First().SpeakerCategory, g.Sum(Def), g.Sum(Fem)))
            .OrderByDescending(s => s.DefaultWords)
            .ToList();

        if (!nodeById.ContainsKey(root))
            return new PathStatsReport(significant, defaultTotal, femaleTotal, 0, 0, 0, 0,
                wordsPerSpeaker, [], [], graph.Unfollowed, spanned);

        // ── Out-edges: two SEPARATE sets ──────────────────────────────────
        // Links are ALTERNATIVES (the player takes one); jumps are SPAWNS (they all happen,
        // because ConversationManager.StartConversation adds a FlowChartPlayer without
        // stopping the current one). Merging them into one edge list would destroy exactly
        // the distinction the additive arithmetic below depends on.
        var jumpsByFrom = graph.Jumps
            .GroupBy(j => j.From)
            .ToDictionary(g => g.Key, g => g.Select(j => j.To).ToList());

        List<NodeRef> LinksOf(NodeRef u) => nodeById[u].Links
            .Select(l => new NodeRef(u.Conversation, l.ToNodeId))
            .Where(nodeById.ContainsKey)          // drop dangling
            .ToList();

        List<NodeRef> JumpsOf(NodeRef u) =>
            jumpsByFrom.TryGetValue(u, out var js)
                ? js.Where(nodeById.ContainsKey).ToList()
                : [];

        // ── Break to a DAG (drop back-edges to a DFS ancestor) ────────────
        var dagLinks = new Dictionary<NodeRef, List<NodeRef>>();
        var dagJumps = new Dictionary<NodeRef, List<NodeRef>>();
        var onStack  = new HashSet<NodeRef>();
        var visited  = new HashSet<NodeRef>();
        void Dfs(NodeRef u)
        {
            visited.Add(u);
            onStack.Add(u);
            dagLinks[u] = [];
            dagJumps[u] = [];
            foreach (var v in LinksOf(u))
            {
                if (onStack.Contains(v)) continue;   // back-edge → drop
                dagLinks[u].Add(v);
                if (!visited.Contains(v)) Dfs(v);
            }
            foreach (var v in JumpsOf(u))
            {
                // A handoff that loops back to a conversation already on the stack is cut
                // by the same rule as a hub loop: counted once, not unrolled.
                if (onStack.Contains(v)) continue;
                dagJumps[u].Add(v);
                if (!visited.Contains(v)) Dfs(v);
            }
            onStack.Remove(u);
        }
        Dfs(root);

        // Memoised longest/shortest weighted path on the DAG (one memo per weight fn).
        var longMemo  = new Dictionary<(NodeRef, bool), int>();
        var shortMemo = new Dictionary<(NodeRef, bool), int>();

        int Longest(NodeRef u, bool female)
        {
            if (longMemo.TryGetValue((u, female), out var cached)) return cached;
            var best = Weight(u, female);
            // Links are ALTERNATIVES: the player takes one, so take the max.
            if (dagLinks.TryGetValue(u, out var outs) && outs.Count > 0)
                best += outs.Max(v => Longest(v, female));
            // Jumps are SPAWNS: ConversationManager.StartConversation adds a new
            // FlowChartPlayer and never stops the current one, so a handoff's words are
            // read IN ADDITION to whatever this node's own links contribute. Summed, not
            // maxed. NodeThatContinuesAndHandsOff_CountsBoth_NotMax pins this.
            if (dagJumps.TryGetValue(u, out var js) && js.Count > 0)
                best += js.Sum(v => Longest(v, female));
            longMemo[(u, female)] = best;
            return best;
        }
        int Shortest(NodeRef u, bool female)
        {
            if (shortMemo.TryGetValue((u, female), out var cached)) return cached;
            var best = Weight(u, female);
            if (dagLinks.TryGetValue(u, out var outs) && outs.Count > 0)
                best += outs.Min(v => Shortest(v, female));
            // Summed here too: a spawn is not optional, so there is no shorter read that
            // skips it.
            if (dagJumps.TryGetValue(u, out var js) && js.Count > 0)
                best += js.Sum(v => Shortest(v, female));
            shortMemo[(u, female)] = best;
            return best;
        }

        // Reachable-set content sum on the FULL graph (cycle-safe via visited set).
        int ReachableSum(NodeRef start, bool female)
        {
            var seen  = new HashSet<NodeRef> { start };
            var queue = new Queue<NodeRef>();
            queue.Enqueue(start);
            var sum = 0;
            while (queue.Count > 0)
            {
                var u = queue.Dequeue();
                sum += Weight(u, female);
                foreach (var v in LinksOf(u).Concat(JumpsOf(u)))
                    if (seen.Add(v)) queue.Enqueue(v);
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
        List<NodeRef> ChoiceFrontier(NodeRef start)
        {
            var seen  = new HashSet<NodeRef> { start };
            var queue = new Queue<NodeRef>();
            var found = new List<NodeRef>();
            void Enqueue(NodeRef u)
            {
                // Crosses handoffs: a jump whose entry node is a player choice puts a fork
                // in another conversation into the tree, which is the point.
                foreach (var v in LinksOf(u).Concat(JumpsOf(u)))
                    if (seen.Add(v)) queue.Enqueue(v);
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
        var forkStack = new HashSet<NodeRef>();
        List<BranchStat> Forks(NodeRef start, int depth)
        {
            if (depth > MaxForkDepth) return [];
            var result = new List<BranchStat>();
            var frontier = ChoiceFrontier(start)
                .Where(c => !forkStack.Contains(c))
                // Deterministic, and the open conversation's own forks stay on top.
                .OrderBy(c => c.Conversation == graph.RootConversation ? 0 : 1)
                .ThenBy(c => c.Conversation, StringComparer.Ordinal)
                .ThenBy(c => c.NodeId);
            foreach (var c in frontier)
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
        var branches = Forks(root, 1);

        // ── Endings (issue #14) ───────────────────────────────────────────
        // Root-to-ending figures are the mirror of Longest/Shortest: walk the DAG's edges
        // backwards. The root has no DAG predecessors by construction — it is on the DFS
        // stack for the whole traversal, so every edge into it is a dropped back-edge —
        // which makes the recursion well-founded without a separate base case.
        var preds = new Dictionary<NodeRef, List<NodeRef>>();
        foreach (var (u, outs) in dagLinks.Concat(dagJumps))
            foreach (var v in outs)
            {
                if (!preds.TryGetValue(v, out var list)) preds[v] = list = [];
                list.Add(u);
            }

        var longToMemo  = new Dictionary<(NodeRef, bool), int>();
        var shortToMemo = new Dictionary<(NodeRef, bool), int>();
        int LongestTo(NodeRef u, bool female)
        {
            if (longToMemo.TryGetValue((u, female), out var cached)) return cached;
            var best = Weight(u, female);
            if (preds.TryGetValue(u, out var ins) && ins.Count > 0)
                best += ins.Max(p => LongestTo(p, female));
            longToMemo[(u, female)] = best;
            return best;
        }
        int ShortestTo(NodeRef u, bool female)
        {
            if (shortToMemo.TryGetValue((u, female), out var cached)) return cached;
            var best = Weight(u, female);
            if (preds.TryGetValue(u, out var ins) && ins.Count > 0)
                best += ins.Min(p => ShortestTo(p, female));
            shortToMemo[(u, female)] = best;
            return best;
        }

        var endings = dagLinks.Keys
            // No links AND no handoff: a node that hands off is a way the conversation
            // CONTINUES, not a way it finishes (#14).
            .Where(id => nodeById[id].Links.Count == 0 && JumpsOf(id).Count == 0)
            .Select(id => new EndingStat(
                id, nodeById[id].DefaultText ?? "",
                LongestTo(id, female: false), ShortestTo(id, female: false),
                LongestTo(id, female: true),  ShortestTo(id, female: true)))
            .OrderByDescending(e => e.DefaultLongestWords)
            .ThenBy(e => e.Node.NodeId)
            .ToList();

        return new PathStatsReport(
            significant, defaultTotal, femaleTotal,
            Longest(root, false),  Shortest(root, false),
            Longest(root, true),   Shortest(root, true),
            wordsPerSpeaker, branches, endings,
            graph.Unfollowed, spanned);
    }
}
