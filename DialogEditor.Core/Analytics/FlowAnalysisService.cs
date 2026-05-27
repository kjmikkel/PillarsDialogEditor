using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Analytics;

public static class FlowAnalysisService
{
    public static FlowAnalysisReport Analyze(ConversationEditSnapshot snapshot)
    {
        var nodes = snapshot.Nodes;
        if (nodes.Count == 0)
            return new FlowAnalysisReport(
                new FlowStatistics(0, 0, 0, 0, 0, 0, 0, 0.0, 0, 0),
                []);

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

        // ── BFS from root (NodeId 0) ──────────────────────────────────────
        var reachable = new HashSet<int>();
        var depth     = new Dictionary<int, int>();
        var queue     = new Queue<int>();

        if (nodes.Any(n => n.NodeId == 0))
        {
            queue.Enqueue(0);
            reachable.Add(0);
            depth[0] = 0;
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!linksByFrom.TryGetValue(current, out var links)) continue;
            foreach (var link in links)
            {
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
            if (!string.IsNullOrEmpty(node.DefaultText))
                wordCount += node.DefaultText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

            switch (node.SpeakerCategory)
            {
                case SpeakerCategory.Player:   playerCount++;   break;
                case SpeakerCategory.Npc:      npcCount++;      break;
                case SpeakerCategory.Narrator: narratorCount++; break;
                case SpeakerCategory.Script:   scriptCount++;   break;
            }

            // Issues
            if (!reachable.Contains(node.NodeId))
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.Unreachable));

            if (node.IsPlayerChoice && node.Links.Count == 0)
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.PlayerDeadEnd));

            if (node.SpeakerCategory != SpeakerCategory.Script
                && string.IsNullOrWhiteSpace(node.DefaultText)
                && string.IsNullOrWhiteSpace(node.FemaleText))
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.EmptyText));

            if (node.NodeId != 0 && !nodesWithIncoming.Contains(node.NodeId))
                issues.Add(new FlowIssue(node.NodeId, FlowIssueKind.NoIncomingLinks));

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
        return new FlowAnalysisReport(stats, issues);
    }
}
