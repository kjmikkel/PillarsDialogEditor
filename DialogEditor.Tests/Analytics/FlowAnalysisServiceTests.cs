using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Analytics;

public class FlowAnalysisServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static NodeEditSnapshot MakeNode(
        int id,
        SpeakerCategory category   = SpeakerCategory.Npc,
        bool isPlayerChoice        = false,
        string defaultText         = "",
        string femaleText          = "",
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, isPlayerChoice, category,
            "", "", defaultText, femaleText,
            "Conversation", "None", "", "", "", false, false,
            links ?? [], [], []);

    private static LinkEditSnapshot Link(int from, int to, bool hasConditions = false) =>
        new(from, to, 1f, "", hasConditions);

    private static ConversationEditSnapshot Snapshot(params NodeEditSnapshot[] nodes) =>
        new(nodes);

    // ── Statistics ────────────────────────────────────────────────────────

    [Fact]
    public void Analyze_EmptySnapshot_ReturnsZeroStats()
    {
        var report = FlowAnalysisService.Analyze(Snapshot());

        Assert.Equal(0, report.Statistics.TotalNodes);
        Assert.Equal(0, report.Statistics.WordCount);
        Assert.Equal(0, report.Statistics.MaxDepth);
        Assert.Equal(0.0, report.Statistics.AvgLinksPerNode);
    }

    [Fact]
    public void Analyze_WordCount_SumsDefaultText()
    {
        var snapshot = Snapshot(
            MakeNode(0, defaultText: "Hello world"),
            MakeNode(1, defaultText: "Three words here"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(5, report.Statistics.WordCount);
    }

    [Fact]
    public void Analyze_NodeTypeCounts_CorrectBySpeakerCategory()
    {
        var snapshot = Snapshot(
            MakeNode(0, SpeakerCategory.Npc),
            MakeNode(1, SpeakerCategory.Player),
            MakeNode(2, SpeakerCategory.Narrator),
            MakeNode(3, SpeakerCategory.Script));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(1, report.Statistics.NpcCount);
        Assert.Equal(1, report.Statistics.PlayerCount);
        Assert.Equal(1, report.Statistics.NarratorCount);
        Assert.Equal(1, report.Statistics.ScriptCount);
    }

    [Fact]
    public void Analyze_MaxDepth_CorrectForLinearChain()
    {
        // 0 → 1 → 2 → 3
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, links: [Link(1, 2)]),
            MakeNode(2, links: [Link(2, 3)]),
            MakeNode(3));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(3, report.Statistics.MaxDepth);
    }

    [Fact]
    public void Analyze_MaxDepth_CorrectForBranchingGraph()
    {
        // 0 → 1 → 3
        // 0 → 2
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1), Link(0, 2)]),
            MakeNode(1, links: [Link(1, 3)]),
            MakeNode(2),
            MakeNode(3));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(2, report.Statistics.MaxDepth);
    }

    [Fact]
    public void Analyze_AvgLinksPerNode_CorrectForMixedGraph()
    {
        // 0 has 2 links, 1 has 1 link, 2 has 0 links → total 3 links / 3 nodes = 1.0
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1), Link(0, 2)]),
            MakeNode(1, links: [Link(1, 2)]),
            MakeNode(2));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(1.0, report.Statistics.AvgLinksPerNode, precision: 5);
        Assert.Equal(3, report.Statistics.TotalLinkCount);
    }

    [Fact]
    public void Analyze_ConditionalLinks_CountedCorrectly()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1, hasConditions: true), Link(0, 2, hasConditions: false)]),
            MakeNode(1),
            MakeNode(2));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(1, report.Statistics.ConditionalLinkCount);
        Assert.Equal(2, report.Statistics.TotalLinkCount);
    }

    // ── Reachability ──────────────────────────────────────────────────────

    [Fact]
    public void Analyze_AllNodesReachable_NoUnreachableIssues()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.Unreachable));
    }

    [Fact]
    public void Analyze_NodeNotReachableFromRoot_FlagsUnreachable()
    {
        // Node 2 is not linked from anywhere
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1),
            MakeNode(2));

        var report = FlowAnalysisService.Analyze(snapshot);

        var issue = Assert.Single(report.Issues.Where(i => i.Kind == FlowIssueKind.Unreachable));
        Assert.Equal(2, issue.NodeId);
    }

    [Fact]
    public void Analyze_CycleInGraph_DoesNotInfiniteLoop()
    {
        // 0 → 1 → 2 → 1 (cycle)
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, links: [Link(1, 2)]),
            MakeNode(2, links: [Link(2, 1)]));

        // Should complete without hanging
        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.Unreachable));
    }

    // ── Issue: PlayerDeadEnd ──────────────────────────────────────────────

    [Fact]
    public void Analyze_NpcDeadEnd_NotFlagged()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Npc)); // NPC with no outgoing links — intentional end

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.PlayerDeadEnd));
    }

    [Fact]
    public void Analyze_PlayerDeadEnd_FlagsDeadEnd()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Player, isPlayerChoice: true)); // no outgoing links

        var report = FlowAnalysisService.Analyze(snapshot);

        var issue = Assert.Single(report.Issues.Where(i => i.Kind == FlowIssueKind.PlayerDeadEnd));
        Assert.Equal(1, issue.NodeId);
    }

    // ── Issue: EmptyText ──────────────────────────────────────────────────

    [Fact]
    public void Analyze_EmptyTextOnNpcNode_FlagsEmptyText()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Npc, defaultText: "", femaleText: ""));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Contains(report.Issues, i => i.Kind == FlowIssueKind.EmptyText && i.NodeId == 1);
    }

    [Fact]
    public void Analyze_EmptyTextOnScriptNode_NotFlagged()
    {
        var snapshot = Snapshot(
            MakeNode(0, defaultText: "Hello", links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Script, defaultText: "", femaleText: ""));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.EmptyText));
    }

    [Fact]
    public void Analyze_NonEmptyTextOnNpcNode_NotFlagged()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Npc, defaultText: "Hello"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.EmptyText && i.NodeId == 1));
    }

    [Fact]
    public void Analyze_WhitespaceTextOnNpcNode_FlagsEmptyText()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Npc, defaultText: "   ", femaleText: "\t"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Contains(report.Issues, i => i.Kind == FlowIssueKind.EmptyText && i.NodeId == 1);
    }

    [Fact]
    public void Analyze_EmptyDefaultTextButFemaleTextPresent_NotFlagged()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, SpeakerCategory.Npc, defaultText: "", femaleText: "Hello"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.EmptyText && i.NodeId == 1));
    }

    // ── Issue: NoIncomingLinks ────────────────────────────────────────────

    [Fact]
    public void Analyze_NodeWithNoIncomingLinks_Flagged()
    {
        // Node 2 has no incoming links (neither 0 nor 1 points to it)
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1),
            MakeNode(2, links: [Link(2, 1)])); // has outgoing but no incoming

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Contains(report.Issues, i => i.Kind == FlowIssueKind.NoIncomingLinks && i.NodeId == 2);
    }

    [Fact]
    public void Analyze_RootNode_NoIncomingLinks_NotFlagged()
    {
        var snapshot = Snapshot(MakeNode(0));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.NoIncomingLinks));
    }

    // ── Issue ordering ────────────────────────────────────────────────────

    [Fact]
    public void Analyze_Issues_SortedByNodeId()
    {
        var snapshot = Snapshot(
            MakeNode(0),   // unreachable: none (it's root); empty text: yes
            MakeNode(5),   // unreachable: yes (not linked from root)
            MakeNode(3));  // unreachable: yes; no incoming: yes

        var report = FlowAnalysisService.Analyze(snapshot);

        var ids = report.Issues.Select(i => i.NodeId).ToList();
        Assert.Equal(ids.OrderBy(x => x).ToList(), ids);
    }

    // ── Edge case: no root node ───────────────────────────────────────────

    [Fact]
    public void Analyze_NoRootNode_AllNodesUnreachable()
    {
        // No NodeId 0 in the snapshot — all nodes should be flagged Unreachable
        var snapshot = Snapshot(
            MakeNode(1, links: [Link(1, 2)]),
            MakeNode(2));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Equal(2, report.Issues.Count(i => i.Kind == FlowIssueKind.Unreachable));
    }
}
