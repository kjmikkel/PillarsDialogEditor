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
}
