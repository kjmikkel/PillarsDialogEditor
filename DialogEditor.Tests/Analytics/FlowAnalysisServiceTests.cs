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
        string displayType         = "Conversation",
        IReadOnlyList<LinkEditSnapshot>? links = null,
        IReadOnlyList<ScriptCall>? scripts = null) =>
        new(id, isPlayerChoice, category,
            "", "", defaultText, femaleText,
            displayType, "None", "", "", "", false, false,
            links ?? [], [], scripts ?? []);

    private static LinkEditSnapshot Link(int from, int to, bool hasConditions = false) =>
        new(from, to, 1, "", hasConditions);

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

    // ── Edge case: no start node (issue 172) ──────────────────────────────

    [Fact]
    public void Analyze_NoNode0_ReportsNoStartNode_AndNoUnreachable()
    {
        var snapshot = Snapshot(
            MakeNode(1, defaultText: "a", links: [Link(1, 2)]),
            MakeNode(2, defaultText: "b"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.False(report.HasStartNode);
        Assert.DoesNotContain(report.Issues, i => i.Kind == FlowIssueKind.Unreachable);
    }

    [Fact]
    public void Analyze_NoNode0_OtherChecksStillRun()
    {
        var snapshot = Snapshot(
            MakeNode(1, links: [Link(1, 2)]),                    // empty text
            MakeNode(2, isPlayerChoice: true, defaultText: "b")); // player dead end

        var kinds = FlowAnalysisService.Analyze(snapshot).Issues.Select(i => i.Kind).ToList();

        Assert.Contains(FlowIssueKind.EmptyText, kinds);
        Assert.Contains(FlowIssueKind.PlayerDeadEnd, kinds);
        Assert.Contains(FlowIssueKind.NoIncomingLinks, kinds);   // node 1: the would-be start
    }

    [Fact]
    public void Analyze_WithNode0_HasStartNode() =>
        Assert.True(FlowAnalysisService.Analyze(Snapshot(MakeNode(0, defaultText: "a"))).HasStartNode);

    [Fact]
    public void Analyze_Empty_HasStartNode() =>
        Assert.True(FlowAnalysisService.Analyze(Snapshot()).HasStartNode);

    // ── Issue: BarkTextTooLong ────────────────────────────────────────

    [Fact]
    public void Analyze_BarkNode_LongText_EmitsBarkTextTooLongIssue()
    {
        var longText = new string('x', BarkConstants.TextLengthWarningThreshold + 1);
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, defaultText: longText, displayType: "Bark"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Contains(report.Issues, i => i.Kind == FlowIssueKind.BarkTextTooLong && i.NodeId == 1);
    }

    [Fact]
    public void Analyze_BarkNode_ShortText_NoBarkTextIssue()
    {
        var shortText = new string('x', BarkConstants.TextLengthWarningThreshold);
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, defaultText: shortText, displayType: "Bark"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.BarkTextTooLong));
    }

    [Fact]
    public void Analyze_ConversationNode_LongText_NoIssue()
    {
        var longText = new string('x', BarkConstants.TextLengthWarningThreshold + 1);
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, defaultText: longText, displayType: "Conversation"));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.BarkTextTooLong));
    }

    // ── Issue: BarkHasPlayerChoiceChild ──────────────────────────────

    [Fact]
    public void Analyze_BarkNode_WithPlayerChoiceChild_EmitsBarkHasPlayerChoiceChildIssue()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, displayType: "Bark", links: [Link(1, 2)]),
            MakeNode(2, isPlayerChoice: true));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Contains(report.Issues,
            i => i.Kind == FlowIssueKind.BarkHasPlayerChoiceChild && i.NodeId == 1);
    }

    [Fact]
    public void Analyze_BarkNode_WithNpcChild_NoBarkChildIssue()
    {
        var snapshot = Snapshot(
            MakeNode(0, links: [Link(0, 1)]),
            MakeNode(1, displayType: "Bark", links: [Link(1, 2)]),
            MakeNode(2, SpeakerCategory.Npc));

        var report = FlowAnalysisService.Analyze(snapshot);

        Assert.Empty(report.Issues.Where(i => i.Kind == FlowIssueKind.BarkHasPlayerChoiceChild));
    }

    // ── Conversation handoffs (#14) ───────────────────────────────────────

    private static ScriptCall StartConversation() =>
        new("Void StartConversation(Guid, Guid, Int32)",
            ["speaker", "conv", "0"], ScriptCategory.Exit);

    [Fact]
    public void JumpWhileContinuing_IsReported()
    {
        var report = FlowAnalysisService.Analyze(Snapshot(
            MakeNode(0, defaultText: "hands off and continues",
                     links: [Link(0, 1)], scripts: [StartConversation()]),
            MakeNode(1, defaultText: "more")));

        Assert.Contains(report.Issues,
            i => i.NodeId == 0 && i.Kind == FlowIssueKind.ConversationJumpWhileContinuing);
    }

    [Fact]
    public void CleanHandoff_TerminalNode_IsNotReported()
    {
        var report = FlowAnalysisService.Analyze(Snapshot(
            MakeNode(0, defaultText: "hands off", scripts: [StartConversation()])));

        Assert.DoesNotContain(report.Issues,
            i => i.Kind == FlowIssueKind.ConversationJumpWhileContinuing);
    }

    // MarkConversationNodeAsRead has the SAME parameter shape as StartConversation — a
    // Conversation lookup kind plus a "Conversation Node ID" — but starts nothing. Guards
    // against anyone replacing the verb whitelist with parameter-shape matching.
    [Fact]
    public void MarkConversationNodeAsRead_IsNotAJump()
    {
        var mark = new ScriptCall("Void MarkConversationNodeAsRead(Guid, Int32)",
                                  ["conv", "3"], ScriptCategory.Enter);
        var report = FlowAnalysisService.Analyze(Snapshot(
            MakeNode(0, defaultText: "reads a flag", links: [Link(0, 1)], scripts: [mark]),
            MakeNode(1, defaultText: "more")));

        Assert.DoesNotContain(report.Issues,
            i => i.Kind == FlowIssueKind.ConversationJumpWhileContinuing);
    }

    // ── Script-node scripts Deadfire never runs (#167) ────────────────────
    // FlowChart.MoveToNode: entering a ScriptNode runs its OnEnter scripts, then moves
    // straight on to the next node, so the script node is never the current node. Its
    // OnExit scripts run only when no link can be followed; its OnUpdate scripts never.

    private static ScriptCall Script(ScriptCategory category) =>
        new("Void SetGlobalValue(String, Int32)", ["g", "1"], category);

    private static NodeEditSnapshot ScriptNode(int id, ScriptNodeKind kind = ScriptNodeKind.Script,
        IReadOnlyList<LinkEditSnapshot>? links = null, params ScriptCall[] scripts) =>
        MakeNode(id, SpeakerCategory.Script, links: links, scripts: scripts) with { ScriptKind = kind };

    private static IEnumerable<FlowIssueKind> ScriptIssues(string gameId, params NodeEditSnapshot[] nodes) =>
        FlowAnalysisService.Analyze(Snapshot(nodes), gameId).Issues
            .Where(i => i.Kind is FlowIssueKind.ScriptExitNeverRuns or FlowIssueKind.ScriptUpdateNeverRuns)
            .Select(i => i.Kind)
            .Order(); // Analyze sorts by node only, and List.Sort is not stable

    [Fact]
    public void Poe2ScriptNode_ThatLeadsOn_FlagsItsExitScripts()
    {
        var kinds = ScriptIssues("poe2",
            ScriptNode(0, links: [Link(0, 1)], scripts: Script(ScriptCategory.Exit)),
            MakeNode(1, defaultText: "next"));
        Assert.Equal([FlowIssueKind.ScriptExitNeverRuns], kinds);
    }

    // With conditions on every link the exit scripts run whenever all of them fail, but the
    // warning still applies: the wording says they run only when no link can be followed.
    [Fact]
    public void Poe2ScriptNode_WithOnlyConditionalLinks_StillFlagsItsExitScripts()
    {
        var kinds = ScriptIssues("poe2",
            ScriptNode(0, links: [Link(0, 1, hasConditions: true)], scripts: Script(ScriptCategory.Exit)),
            MakeNode(1, defaultText: "next"));
        Assert.Equal([FlowIssueKind.ScriptExitNeverRuns], kinds);
    }

    [Fact]
    public void Poe2ScriptNode_WithNoLinks_RunsItsExitScripts()
    {
        Assert.Empty(ScriptIssues("poe2", ScriptNode(0, scripts: Script(ScriptCategory.Exit))));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Poe2ScriptNode_FlagsUpdateScripts_WithOrWithoutLinks(bool leadsOn)
    {
        var kinds = ScriptIssues("poe2",
            ScriptNode(0, links: leadsOn ? [Link(0, 1)] : [], scripts: Script(ScriptCategory.Update)),
            MakeNode(1, defaultText: "next"));
        Assert.Equal([FlowIssueKind.ScriptUpdateNeverRuns], kinds);
    }

    [Fact]
    public void Poe2ScriptNode_WithBothKinds_ReportsEachOnce()
    {
        var kinds = ScriptIssues("poe2",
            ScriptNode(0, links: [Link(0, 1)], scripts:
                [Script(ScriptCategory.Exit), Script(ScriptCategory.Exit), Script(ScriptCategory.Update)]),
            MakeNode(1, defaultText: "next"));
        Assert.Equal([FlowIssueKind.ScriptExitNeverRuns, FlowIssueKind.ScriptUpdateNeverRuns], kinds);
    }

    [Fact]
    public void Poe2ScriptNode_WithOnlyEnterScripts_IsNotFlagged()
    {
        Assert.Empty(ScriptIssues("poe2",
            ScriptNode(0, links: [Link(0, 1)], scripts: Script(ScriptCategory.Enter)),
            MakeNode(1, defaultText: "next")));
    }

    // Bank and trigger-conversation nodes have their own branches in MoveToNode, and both
    // run their exit scripts.
    [Theory]
    [InlineData(ScriptNodeKind.Bank)]
    [InlineData(ScriptNodeKind.TriggerConversation)]
    public void Poe2BankAndTriggerNodes_AreNotFlagged(ScriptNodeKind kind)
    {
        Assert.Empty(ScriptIssues("poe2",
            ScriptNode(0, kind, links: [Link(0, 1)],
                       scripts: [Script(ScriptCategory.Exit), Script(ScriptCategory.Update)]),
            MakeNode(1, defaultText: "next")));
    }

    // PoE1's MoveToNode has no ScriptNode branch: a script node becomes the current node and
    // runs its exit scripts on the way out like any other.
    [Theory]
    [InlineData("poe1")]
    [InlineData("")]
    public void NonDeadfireConversations_AreNotFlagged(string gameId)
    {
        Assert.Empty(ScriptIssues(gameId,
            ScriptNode(0, links: [Link(0, 1)], scripts: [Script(ScriptCategory.Exit), Script(ScriptCategory.Update)]),
            MakeNode(1, defaultText: "next")));
    }

    [Fact]
    public void Poe2TalkNode_WithExitAndUpdateScripts_IsNotFlagged()
    {
        Assert.Empty(ScriptIssues("poe2",
            MakeNode(0, defaultText: "hi", links: [Link(0, 1)],
                     scripts: [Script(ScriptCategory.Exit), Script(ScriptCategory.Update)]),
            MakeNode(1, defaultText: "next")));
    }
}
