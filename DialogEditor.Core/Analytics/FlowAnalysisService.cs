using DialogEditor.Core.Conditions;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Analytics;

public static class FlowAnalysisService
{
    /// <param name="gameId">"poe1", "poe2" or "" — some checks apply to one game only.</param>
    public static FlowAnalysisReport Analyze(ConversationEditSnapshot snapshot, string gameId = "")
    {
        var nodes = snapshot.Nodes;
        if (nodes.Count == 0)
            return new FlowAnalysisReport(
                new FlowStatistics(0, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
                []);

        var hasStartNode = StartNode.IsPresent(nodes.Select(n => n.NodeId));

        // ── Build adjacency and incoming-link sets ────────────────────────
        var linksByFrom      = nodes.ToDictionary(n => n.NodeId, n => n.Links);
        var nodeById         = nodes.ToDictionary(n => n.NodeId);
        var nodesWithIncoming = new HashSet<int>();
        var totalLinks       = 0;
        var conditionalLinks = 0;

        foreach (var node in nodes)
        {
            foreach (var link in node.Links)
            {
                nodesWithIncoming.Add(link.ToNodeId);
                totalLinks++;
                if (link.HasConditions) conditionalLinks++;
            }
        }

        // ── Links an earlier always-passing link hides (issue 353) ────────
        // Judged against an empty game state, so only links certain to pass count as hiders.
        // The BFS skips hidden links: a node reachable only through one can never be reached.
        var hiddenLinks = new Dictionary<int, IReadOnlyList<int?>>();
        foreach (var node in nodes)
            hiddenLinks[node.NodeId] = LinkShadowing.HiddenBy(
                node, id => nodeById.GetValueOrDefault(id), new GameState(), gameId);

        // ── BFS from the start node (StartNode.Id) ────────────────────────
        var reachable = new HashSet<int>();
        var depth     = new Dictionary<int, int>();
        var queue     = new Queue<int>();

        if (hasStartNode)
        {
            queue.Enqueue(StartNode.Id);
            reachable.Add(StartNode.Id);
            depth[StartNode.Id] = 0;
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!linksByFrom.TryGetValue(current, out var links)) continue;
            var hidden = hiddenLinks[current];
            for (int i = 0; i < links.Count; i++)
            {
                if (hidden[i] is not null) continue;
                var link = links[i];
                if (reachable.Add(link.ToNodeId))
                {
                    depth[link.ToNodeId] = depth[current] + 1;
                    queue.Enqueue(link.ToNodeId);
                }
            }
        }

        // ── Single pass: statistics + issues ─────────────────────────────
        var issues        = new List<FlowIssue>();
        var wordCount     = 0;
        var playerCount   = 0;
        var npcCount      = 0;
        var narratorCount = 0;
        var scriptCount   = 0;

        foreach (var node in nodes)
        {
            // Statistics
            wordCount += WordCount.Of(node.DefaultText);

            switch (node.SpeakerCategory)
            {
                case SpeakerCategory.Player:   playerCount++;   break;
                case SpeakerCategory.Npc:      npcCount++;      break;
                case SpeakerCategory.Narrator: narratorCount++; break;
                case SpeakerCategory.Script:   scriptCount++;   break;
            }

            // Issues
            if (hasStartNode && !reachable.Contains(node.NodeId))
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.Unreachable));

            if (node.IsPlayerChoice && node.Links.Count == 0)
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.PlayerDeadEnd));

            if (node.SpeakerCategory != SpeakerCategory.Script
                && string.IsNullOrWhiteSpace(node.DefaultText)
                && string.IsNullOrWhiteSpace(node.FemaleText))
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.EmptyText));

            if (node.NodeId != StartNode.Id && !nodesWithIncoming.Contains(node.NodeId))
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.NoIncomingLinks));

            // A handoff is a SPAWN, not a replace — ConversationManager.StartConversation
            // adds a new FlowChartPlayer without stopping the current one. A node that both
            // links onward and hands off therefore really does run both.
            if (node.Links.Count > 0 &&
                node.Scripts.Any(sc => ConversationJumpVerbs.IsJump(sc.DisplayName)))
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.ConversationJumpWhileContinuing));

            var hiddenBy = hiddenLinks[node.NodeId];
            for (int i = 0; i < hiddenBy.Count; i++)
            {
                if (hiddenBy[i] is not { } hider) continue;
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.LinkShadowed)
                {
                    RelatedNodeId = node.Links[i].ToNodeId,
                    HiderNodeId   = node.Links[hider].ToNodeId
                });
            }

            foreach (var kind in ScriptNodeRules.NeverRun(
                         gameId, node.SpeakerCategory, node.ScriptKind, node.Links.Count > 0, node.Scripts))
                issues.Add(new FlowIssue(node.NodeId, kind));

            if (node.DisplayType == "Bark")
            {
                if (node.DefaultText.Length > BarkConstants.TextLengthWarningThreshold)
                    issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.BarkTextTooLong));

                foreach (var link in node.Links)
                {
                    if (nodeById.TryGetValue(link.ToNodeId, out var target) && target.IsPlayerChoice)
                    {
                        issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.BarkHasPlayerChoiceChild));
                        break;
                    }
                }
            }
        }

        var maxDepth = depth.Count > 0 ? depth.Values.Max() : 0;
        var avgLinks = nodes.Count > 0 ? (double)totalLinks / nodes.Count : 0.0;

        var stats = new FlowStatistics(
            TotalNodes:           nodes.Count,
            WordCount:            wordCount,
            MaxDepth:             maxDepth,
            PlayerCount:          playerCount,
            NpcCount:             npcCount,
            NarratorCount:        narratorCount,
            ScriptCount:          scriptCount,
            AvgLinksPerNode:      avgLinks,
            ConditionalLinkCount: conditionalLinks,
            TotalLinkCount:       totalLinks);

        issues.Sort((a, b) => a.NodeId.CompareTo(b.NodeId));
        return new FlowAnalysisReport(stats, issues) { HasStartNode = hasStartNode };
    }
}
