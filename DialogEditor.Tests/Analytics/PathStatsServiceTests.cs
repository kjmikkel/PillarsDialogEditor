using DialogEditor.Core.Analytics;
using DialogEditor.Core.Conditions;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Analytics;

public class PathStatsServiceTests
{
    private static NodeEditSnapshot Node(
        int id, string defaultText = "", string femaleText = "",
        bool isPlayerChoice = false, string speaker = "",
        SpeakerCategory category = SpeakerCategory.Npc,
        IReadOnlyList<LinkEditSnapshot>? links = null,
        IReadOnlyList<ScriptCall>? scripts = null) =>
        new(id, isPlayerChoice, category, speaker, "", defaultText, femaleText,
            "Conversation", "None", "", "", "", false, false,
            links ?? [], [], scripts ?? []);

    private static LinkEditSnapshot Link(int from, int to) => new(from, to, 1, "", false);
    private static ConversationEditSnapshot Snap(params NodeEditSnapshot[] n) => new(n);

    [Fact]
    public void LongestAndShortest_TwoEndings()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 2)]),
            Node(1, "long",  isPlayerChoice: true, links: [Link(1, 3)]),
            Node(3, "aaa bbb ccc ddd"),           // 4 words
            Node(2, "short", isPlayerChoice: true)));

        Assert.Equal(6, report.DefaultLongestWords);   // start+long+4
        Assert.Equal(2, report.DefaultShortestWords);  // start+short
    }

    [Fact]
    public void HubLoop_Terminates_AndCountsNodeOnce()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a", links: [Link(0, 1)]),
            Node(1, "b", links: [Link(1, 2)]),
            Node(2, "c", links: [Link(2, 0)])));       // back-edge to ancestor 0

        Assert.Equal(3, report.DefaultLongestWords);   // a+b+c, loop cut
    }

    [Fact]
    public void PerOpeningChoice_ContentAndLongest()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 4)]),
            Node(1, "A", isPlayerChoice: true, links: [Link(1, 3)]),
            Node(3, "x y z"),                          // 3 words
            Node(4, "B", isPlayerChoice: true, links: [Link(4, 5)]),
            Node(5, "p")));                            // 1 word

        Assert.Equal(2, report.Branches.Count);
        var a = report.Branches.Single(b => b.Choice.NodeId == 1);
        Assert.Equal(4, a.DefaultContentWords);        // A(1)+x y z(3)
        Assert.Equal(4, a.DefaultLongestWords);
        var b = report.Branches.Single(x => x.Choice.NodeId == 4);
        Assert.Equal(2, b.DefaultContentWords);        // B(1)+p(1)
        Assert.Equal(2, b.DefaultLongestWords);
    }

    [Fact]
    public void WordsPerSpeaker_GroupedByGuid()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "aa bb", speaker: "npc1", links: [Link(0, 1)]),
            Node(1, "cc", speaker: "player", category: SpeakerCategory.Player, links: [Link(1, 2)]),
            Node(2, "dd", speaker: "npc1")));

        Assert.Equal(3, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "npc1").DefaultWords);
        Assert.Equal(1, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "player").DefaultWords);
    }

    [Fact] // GitHub issue 257: blank-GUID nodes of different kinds are different speakers.
    public void WordsPerSpeaker_BlankGuid_GroupedByCategory()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "aa bb", category: SpeakerCategory.Player, links: [Link(0, 1)]),
            Node(1, "cc", category: SpeakerCategory.Script)));

        Assert.Equal(2, report.WordsPerSpeaker.Single(s => s.Category == SpeakerCategory.Player).DefaultWords);
        Assert.Equal(1, report.WordsPerSpeaker.Single(s => s.Category == SpeakerCategory.Script).DefaultWords);
    }

    [Fact]
    public void FemaleGate_NotSignificant_WhenNoFemaleText()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a b", links: [Link(0, 1)]),
            Node(1, "c d")));

        Assert.False(report.HasSignificantFemaleVariant);
        Assert.Equal(report.DefaultTotalWords, report.FemaleTotalWords);
    }

    [Fact]
    public void FemaleGate_Significant_WhenFemaleDiffersOver10Percent()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a", links: [Link(0, 1)]),
            Node(1, "b", femaleText: "one two three four five")));   // 5 vs 1

        Assert.True(report.HasSignificantFemaleVariant);
        Assert.Equal(2, report.DefaultTotalWords);   // a + b
        Assert.Equal(6, report.FemaleTotalWords);    // a + five
    }

    [Fact]
    public void EmptySnapshot_EmptyReport()
    {
        var report = PathStatsService.Analyze(Snap());
        Assert.Equal(0, report.DefaultTotalWords);
        Assert.Empty(report.WordsPerSpeaker);
        Assert.Empty(report.Branches);
    }

    [Fact]
    public void NoRoot_HeaderZero_ButSpeakersCounted()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(5, "hello world", speaker: "x")));   // no node 0

        Assert.Equal(0, report.DefaultLongestWords);
        Assert.Empty(report.Branches);
        Assert.Equal(2, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "x").DefaultWords);
    }

    // ── Choice frontier (issue #14) ──────────────────────────────────────────
    // v1 took root's DIRECT link targets that were player choices, so a conversation
    // opening with an NPC greeting before the choice menu reported no branches at all.

    [Fact]
    public void OpeningChoices_FoundPastAnNpcGreeting()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "hello", links: [Link(0, 1)]),
            Node(1, "how are you", links: [Link(1, 2), Link(1, 3)]),
            Node(2, "A", isPlayerChoice: true),
            Node(3, "B", isPlayerChoice: true)));

        Assert.Equal([2, 3], report.Branches.Select(b => b.Choice.NodeId));
    }

    [Fact]
    public void ChoiceFrontier_StopsAtTheFirstChoice_DoesNotLookPastIt()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1)]),
            Node(1, "A", isPlayerChoice: true, links: [Link(1, 2)]),
            Node(2, "npc", links: [Link(2, 3)]),
            Node(3, "A1", isPlayerChoice: true)));

        // 3 is a fork under 1, never a sibling of it.
        Assert.Equal([1], report.Branches.Select(b => b.Choice.NodeId));
    }

    // ── Recursive fork breakdown (issue #14) ─────────────────────────────────

    [Fact]
    public void SubForks_NestUnderTheirOpeningChoice_WithTheirOwnFigures()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1)]),
            Node(1, "A", isPlayerChoice: true, links: [Link(1, 2)]),
            Node(2, "npc", links: [Link(2, 3), Link(2, 4)]),
            Node(3, "A1", isPlayerChoice: true, links: [Link(3, 5)]),
            Node(5, "x y z"),                                  // 3 words
            Node(4, "A2", isPlayerChoice: true)));

        var a = Assert.Single(report.Branches);
        Assert.Equal([3, 4], a.SubBranches.Select(b => b.Choice.NodeId));

        var a1 = a.SubBranches.Single(b => b.Choice.NodeId == 3);
        Assert.Equal(4, a1.DefaultContentWords);               // A1(1) + x y z(3)
        Assert.Equal(4, a1.DefaultLongestWords);
        Assert.Empty(a1.SubBranches);
    }

    [Fact]
    public void ForkLoopingBackToAnAncestorChoice_IsALeaf_AndTerminates()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1)]),
            Node(1, "A", isPlayerChoice: true, links: [Link(1, 2)]),
            Node(2, "npc", links: [Link(2, 3)]),
            Node(3, "B", isPlayerChoice: true, links: [Link(3, 1)])));   // back to choice 1

        var a = Assert.Single(report.Branches);
        var b = Assert.Single(a.SubBranches);
        Assert.Equal(3, b.Choice.NodeId);
        Assert.Empty(b.SubBranches);      // 1 is on the fork stack — counted once
    }

    [Fact]
    public void ForkRecursion_StopsAtTheDepthCap()
    {
        // A long chain of choices, each leading to the next. Without a cap this nests
        // as deep as the conversation is long; the cap bounds what the panel renders.
        var nodes = new List<NodeEditSnapshot> { Node(0, "start", links: [Link(0, 1)]) };
        for (var i = 1; i <= 40; i++)
            nodes.Add(Node(i, $"c{i}", isPlayerChoice: true, links: [Link(i, i + 1)]));
        nodes.Add(Node(41, "end"));

        var report = PathStatsService.Analyze(new ConversationEditSnapshot(nodes));

        var depth = 0;
        for (var level = report.Branches; level.Count > 0; level = level[0].SubBranches)
            depth++;
        Assert.Equal(PathStatsService.MaxForkDepth, depth);
    }

    // ── Per-ending breakdown (issue #14) ─────────────────────────────────────
    // An ending is a node with no outgoing links: a way the conversation actually
    // finishes. A node whose only exits loop backwards is a DAG terminal but not an
    // ending, and must not be listed as one.

    [Fact]
    public void Endings_ReportedLongestFirst_WithRootToEndingFigures()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 2)]),
            Node(1, "A", isPlayerChoice: true, links: [Link(1, 3)]),
            Node(3, "end one here"),                           // 3 words, dead end
            Node(2, "B", isPlayerChoice: true)));              // dead end

        Assert.Equal([3, 2], report.Endings.Select(e => e.Node.NodeId));

        var deep = report.Endings.Single(e => e.Node.NodeId == 3);
        Assert.Equal("end one here", deep.Text);
        Assert.Equal(5, deep.DefaultLongestWords);             // start+A+3
        Assert.Equal(5, deep.DefaultShortestWords);
        Assert.Equal(2, report.Endings.Single(e => e.Node.NodeId == 2).DefaultLongestWords);
    }

    [Fact]
    public void Ending_ReachedTwoWays_ReportsBothExtremes()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 2)]),
            Node(1, "a a a", isPlayerChoice: true, links: [Link(1, 5)]),
            Node(2, "B",     isPlayerChoice: true, links: [Link(2, 5)]),
            Node(5, "the end")));                              // 2 words, dead end

        var e = Assert.Single(report.Endings);
        Assert.Equal(6, e.DefaultLongestWords);                // start(1)+a a a(3)+the end(2)
        Assert.Equal(4, e.DefaultShortestWords);               // start(1)+B(1)+the end(2)
    }

    [Fact]
    public void LoopOnlyNode_IsNotAnEnding()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a", links: [Link(0, 1)]),
            Node(1, "b", links: [Link(1, 2)]),
            Node(2, "c", links: [Link(2, 0)])));               // exits, but backwards

        Assert.Empty(report.Endings);
    }

    [Fact]
    public void Ending_UnreachableFromRoot_IsExcluded()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1)]),
            Node(1, "the end"),
            Node(9, "orphan line")));                          // dead end, unreachable

        Assert.Equal([1], report.Endings.Select(e => e.Node.NodeId));
    }

    [Fact]
    public void Endings_UseTheFemaleReadingToo()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "a", links: [Link(0, 1)]),
            Node(1, "one", femaleText: "one two three four five")));

        var e = Assert.Single(report.Endings);
        Assert.Equal(2, e.DefaultLongestWords);                // a + one
        Assert.Equal(6, e.FemaleLongestWords);                 // a + five female words
    }

    [Fact]
    public void NoRoot_NoEndings()
    {
        var report = PathStatsService.Analyze(Snap(Node(5, "hello world")));
        Assert.Empty(report.Endings);
    }

    // The load-bearing test of the cross-conversation design (#14): the single-conversation
    // overload must BE the multi-conversation one with a one-conversation graph, not a
    // parallel implementation. If this fails, the two modes have drifted.
    [Fact]
    public void SnapshotOverload_EqualsOneConversationGraph()
    {
        var snap = Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 2)]),
            Node(1, "long", isPlayerChoice: true, links: [Link(1, 3)]),
            Node(3, "aaa bbb ccc ddd"),
            Node(2, "short", isPlayerChoice: true));

        var graph = new MultiConversationGraph(
            RootConversation: "",
            Conversations: new Dictionary<string, ConversationEditSnapshot> { [""] = snap },
            Jumps: [],
            Unfollowed: []);

        // Record equality is no use here: PathStatsReport's collection members are
        // IReadOnlyList, which compares by reference, so two separately-computed reports
        // never compare equal however identical their contents. Flatten to values instead.
        Assert.Equal(Flatten(PathStatsService.Analyze(snap)),
                     Flatten(PathStatsService.Analyze(graph)));
    }

    /// Every number and node key in a report, as value-comparable tuples.
    private static object Flatten(PathStatsReport r) => new
    {
        r.HasSignificantFemaleVariant,
        r.DefaultTotalWords, r.FemaleTotalWords,
        r.DefaultLongestWords, r.DefaultShortestWords,
        r.FemaleLongestWords, r.FemaleShortestWords,
        r.ConversationsSpanned,
        Speakers = string.Join("|", r.WordsPerSpeaker.Select(
            s => $"{s.SpeakerGuid}:{s.Category}:{s.DefaultWords}:{s.FemaleWords}")),
        Branches = string.Join("|", FlattenBranches(r.Branches, 0)),
        Endings  = string.Join("|", r.Endings.Select(
            e => $"{e.Node.Conversation}#{e.Node.NodeId}:{e.DefaultLongestWords}:" +
                 $"{e.DefaultShortestWords}:{e.FemaleLongestWords}:{e.FemaleShortestWords}")),
        Unfollowed = string.Join("|", r.Unfollowed.Select(
            u => $"{u.From.Conversation}#{u.From.NodeId}:{u.TargetLabel}:{u.Reason}")),
    }.ToString()!;

    private static IEnumerable<string> FlattenBranches(IReadOnlyList<BranchStat> bs, int depth) =>
        bs.SelectMany(b => new[]
        {
            $"{depth}:{b.Choice.Conversation}#{b.Choice.NodeId}:{b.DefaultContentWords}:" +
            $"{b.DefaultLongestWords}:{b.FemaleContentWords}:{b.FemaleLongestWords}"
        }.Concat(FlattenBranches(b.SubBranches, depth + 1)));

    // ── Cross-conversation handoffs (#14) ────────────────────────────────────

    private static MultiConversationGraph Graph(
        string root,
        IReadOnlyDictionary<string, ConversationEditSnapshot> convs,
        params JumpEdge[] jumps) => new(root, convs, jumps, []);

    private static Dictionary<string, ConversationEditSnapshot> Convs(
        params (string Name, ConversationEditSnapshot Snap)[] cs) =>
        cs.ToDictionary(c => c.Name, c => c.Snap);

    [Fact]
    public void Handoff_TerminalNodeJumps_LongestSpansBothConversations()
    {
        var a = Snap(Node(0, "one two", links: [Link(0, 1)]), Node(1, "three"));
        var b = Snap(Node(0, "four five six"));

        var report = PathStatsService.Analyze(Graph("A", Convs(("A", a), ("B", b)),
            new JumpEdge(new NodeRef("A", 1), new NodeRef("B", 0))));

        Assert.Equal(6, report.DefaultLongestWords);   // 2 + 1 + 3
        Assert.Equal(2, report.ConversationsSpanned);
    }

    // Pins the ADDITIVE rule against the rejected "treat a jump as one more link" design.
    // The engine spawns a second FlowChartPlayer without stopping the current one, so a
    // node that continues AND hands off really does run both. If someone folds jumps into
    // the ordinary edge list, Longest becomes 2 + max(1, 3) = 5 and this fails.
    // Do not "simplify" it away.
    [Fact]
    public void NodeThatContinuesAndHandsOff_CountsBoth_NotMax()
    {
        var a = Snap(Node(0, "one two", links: [Link(0, 1)]), Node(1, "three"));
        var b = Snap(Node(0, "four five six"));

        var report = PathStatsService.Analyze(Graph("A", Convs(("A", a), ("B", b)),
            new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

        Assert.Equal(6, report.DefaultLongestWords);   // 2 + max(1) + 3, not 2 + max(1, 3)
    }

    [Fact]
    public void Shortest_SumsJumps_BecauseASpawnIsNotOptional()
    {
        // Link to 1 is conditioned (Unknown) so it is a genuine alternative, not hidden by
        // an always-passing earlier link (issue 353).
        var a = Snap(Node(0, "one", links: [GatedLink(0, 1, UnknownCond), Link(0, 2)]),
                     Node(1, "two three four"), Node(2, "five"));
        var b = Snap(Node(0, "six seven"));

        var report = PathStatsService.Analyze(Graph("A", Convs(("A", a), ("B", b)),
            new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

        Assert.Equal(4, report.DefaultShortestWords);  // 1 + min(3, 1) + 2
    }

    [Fact]
    public void JumpCycle_Terminates_AndCountsOnce()
    {
        var a = Snap(Node(0, "a"));
        var b = Snap(Node(0, "b"));

        var report = PathStatsService.Analyze(Graph("A", Convs(("A", a), ("B", b)),
            new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0)),
            new JumpEdge(new NodeRef("B", 0), new NodeRef("A", 0))));

        Assert.Equal(2, report.DefaultLongestWords);
    }

    [Fact]
    public void NodeWithAHandoff_IsNotAnEnding()
    {
        var a = Snap(Node(0, "a"));
        var b = Snap(Node(0, "b"));

        var report = PathStatsService.Analyze(Graph("A", Convs(("A", a), ("B", b)),
            new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

        Assert.DoesNotContain(report.Endings, e => e.Node == new NodeRef("A", 0));
        Assert.Contains(report.Endings,       e => e.Node == new NodeRef("B", 0));
    }

    [Fact]
    public void ForkInsideAJumpedToConversation_AppearsInTheTree()
    {
        var a = Snap(Node(0, "greeting"));
        var b = Snap(Node(0, "hub", links: [Link(0, 1)]),
                     Node(1, "pick me", isPlayerChoice: true));

        var report = PathStatsService.Analyze(Graph("A", Convs(("A", a), ("B", b)),
            new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

        Assert.Contains(report.Branches, x => x.Choice == new NodeRef("B", 1));
    }

    [Fact]
    public void WordsPerSpeaker_SumsOneSpeakerAcrossConversations()
    {
        var a = Snap(Node(0, "one two", speaker: "serafen"));
        var b = Snap(Node(0, "three", speaker: "serafen"));

        var report = PathStatsService.Analyze(Graph("A", Convs(("A", a), ("B", b)),
            new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

        Assert.Equal(3, report.WordsPerSpeaker.Single(s => s.SpeakerGuid == "serafen").DefaultWords);
    }

    // ── Issue 172: no start node ─────────────────────────────────────────

    [Fact]
    public void NoNode0_HasNoStartNode_ButStillCountsWords()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(1, "one two", links: [Link(1, 2)]),
            Node(2, "three")));

        Assert.False(report.HasStartNode);
        Assert.Equal(3, report.DefaultTotalWords);
        Assert.NotEmpty(report.WordsPerSpeaker);
    }

    [Fact]
    public void WithNode0_HasStartNode() =>
        Assert.True(PathStatsService.Analyze(Snap(Node(0, "a"))).HasStartNode);

    [Fact]
    public void Empty_HasStartNode() =>
        Assert.True(PathStatsService.Analyze(Snap()).HasStartNode);

    // ── Condition-aware stats (#32) ───────────────────────────────────────
    // g is 0 in AssumedState(), so "g EqualTo 1" is definitely False and "h EqualTo 1"
    // (h unset) is Unknown.
    private static ConditionLeaf GlobalIs(string name, string value) =>
        new("Boolean IsGlobalValue(String, Operator, Int32)", [name, "EqualTo", value], Not: false, Operator: "And");

    private static readonly IReadOnlyList<ConditionNode> AlwaysFalse = [GlobalIs("g", "1")];
    private static readonly IReadOnlyList<ConditionNode> UnknownCond = [GlobalIs("h", "1")];

    private static GameState AssumedState()
    {
        var s = new GameState();
        s.Globals["g"] = 0;
        return s;
    }

    private static LinkEditSnapshot GatedLink(int from, int to, IReadOnlyList<ConditionNode> c) =>
        Link(from, to) with { Conditions = c };

    private static ConversationEditSnapshot ForkWithLongBranch(IReadOnlyList<ConditionNode> longLinkConditions) =>
        Snap(
            Node(0, "start", links: [GatedLink(0, 1, longLinkConditions), Link(0, 2)]),
            Node(1, "long", isPlayerChoice: true, links: [Link(1, 3)]),
            Node(3, "aaa bbb ccc ddd"),
            Node(2, "short", isPlayerChoice: true));

    [Fact]
    public void BlockedBranch_DropsOutOfLongestAndShortest()
    {
        var report = PathStatsService.Analyze(ForkWithLongBranch(AlwaysFalse), AssumedState(), "poe2");

        Assert.Equal(2, report.DefaultLongestWords);   // start+short; the 6-word path is gone
        Assert.Equal(2, report.DefaultShortestWords);
    }

    [Fact]
    public void BlockedPlayerChoice_LeavesTheForkTree()
    {
        var report = PathStatsService.Analyze(ForkWithLongBranch(AlwaysFalse), AssumedState(), "poe2");

        Assert.Equal(2, Assert.Single(report.Branches).Choice.NodeId);
    }

    [Fact]
    public void NodeWhoseEveryLinkIsBlocked_BecomesAnEnding()
    {
        var snap = Snap(
            Node(0, "start", links: [GatedLink(0, 1, AlwaysFalse)]),
            Node(1, "never"));

        var report = PathStatsService.Analyze(snap, AssumedState(), "poe2");

        Assert.Equal(0, Assert.Single(report.Endings).Node.NodeId);
    }

    [Fact]
    public void UnknownCondition_KeepsTheLink()
    {
        var report = PathStatsService.Analyze(ForkWithLongBranch(UnknownCond), AssumedState(), "poe2");

        Assert.Equal(6, report.DefaultLongestWords);
        Assert.Equal(2, report.Branches.Count);
        Assert.Equal(0, report.BlockedLinkCount);
    }

    [Fact]
    public void NoFacts_FiguresUnchanged()
    {
        var snap = ForkWithLongBranch(AlwaysFalse);

        var without = PathStatsService.Analyze(snap);
        var withEmptyState = PathStatsService.Analyze(snap, new GameState(), "poe2");

        Assert.Equal(without.DefaultLongestWords, withEmptyState.DefaultLongestWords);
        Assert.Equal(without.DefaultShortestWords, withEmptyState.DefaultShortestWords);
        Assert.Equal(without.Branches.Count, withEmptyState.Branches.Count);
        Assert.Equal(without.Endings.Count, withEmptyState.Endings.Count);
        Assert.Equal(0, withEmptyState.BlockedLinkCount);
    }

    [Fact]
    public void BlockedLinkCount_CountsLinksThatCannotFire()
    {
        var snap = Snap(
            Node(0, "start", links: [GatedLink(0, 1, AlwaysFalse), GatedLink(0, 2, AlwaysFalse), Link(0, 3)]),
            Node(1, "a"), Node(2, "b"), Node(3, "c"));

        Assert.Equal(2, PathStatsService.Analyze(snap, AssumedState(), "poe2").BlockedLinkCount);
        Assert.Equal(0, PathStatsService.Analyze(snap).BlockedLinkCount);
    }

    [Fact]
    public void FalseTargetNodeConditions_BlockTheLinkIntoIt()
    {
        var snap = Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 2)]),
            Node(1, "gated node", isPlayerChoice: true) with { Conditions = AlwaysFalse },
            Node(2, "open", isPlayerChoice: true));

        var report = PathStatsService.Analyze(snap, AssumedState(), "poe2");

        Assert.Equal(2, Assert.Single(report.Branches).Choice.NodeId);
        Assert.Equal(1, report.BlockedLinkCount);
    }

    [Fact]
    public void Handoff_IsNeverPruned()
    {
        var a = Snap(
            Node(0, "start", links: [GatedLink(0, 1, AlwaysFalse)]),
            Node(1, "never"));
        var b = Snap(Node(0, "other conversation"));
        var graph = new MultiConversationGraph(
            RootConversation: "A",
            Conversations: new Dictionary<string, ConversationEditSnapshot> { ["A"] = a, ["B"] = b },
            Jumps: [new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))],
            Unfollowed: []);

        var report = PathStatsService.Analyze(graph, AssumedState(), "poe2");

        Assert.Equal(3, report.DefaultLongestWords);   // start(1) + handoff target "other conversation"(2)
        Assert.DoesNotContain(report.Endings, e => e.Node.NodeId == 0 && e.Node.Conversation == "A");
    }

    // ── First-passing-link rule (GitHub issue 353) ────────────────────────
    // A link that always passes hides the links after it, with or without an assumed state.

    [Fact]
    public void ShadowedBranch_LeavesLongestAndShortest()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 2)]),
            Node(1, "a"),
            Node(2, "b b b b b")));

        Assert.Equal(2, report.DefaultLongestWords);   // start+a; the long link never fires
        Assert.Equal(2, report.DefaultShortestWords);
    }

    [Fact]
    public void ShadowedChoice_LeavesForkTree()
    {
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 2)]),
            Node(1, "npc"),
            Node(2, "choice", isPlayerChoice: true)));

        Assert.Empty(report.Branches);
    }

    [Fact]
    public void ShadowedLinkCount_CountsHiddenLinks()
    {
        var snap = Snap(
            Node(0, "start", links: [Link(0, 1), Link(0, 2), Link(0, 3)]),
            Node(1, "a"), Node(2, "b"), Node(3, "c"));

        Assert.Equal(2, PathStatsService.Analyze(snap).ShadowedLinkCount);
        Assert.Equal(2, PathStatsService.Analyze(snap, AssumedState(), "poe2").ShadowedLinkCount);
    }

    [Fact]
    public void NoHidingLinks_FiguresUnchanged()
    {
        // Every link is conditioned on an unset global, so none is certain.
        var report = PathStatsService.Analyze(Snap(
            Node(0, "start", links: [GatedLink(0, 1, UnknownCond), GatedLink(0, 2, UnknownCond)]),
            Node(1, "long", isPlayerChoice: true, links: [Link(1, 3)]),
            Node(3, "aaa bbb ccc ddd"),
            Node(2, "short", isPlayerChoice: true)));

        Assert.Equal(0, report.ShadowedLinkCount);
        Assert.Equal(6, report.DefaultLongestWords);
        Assert.Equal(2, report.DefaultShortestWords);
        Assert.Equal(2, report.Branches.Count);
        Assert.Equal(2, report.Endings.Count);
    }

    [Fact]
    public void ShadowingUsesAssumedState()
    {
        var snap = Snap(
            Node(0, "start", links: [GatedLink(0, 1, [GlobalIs("g", "1")]), Link(0, 2)]),
            Node(1, "a"),
            Node(2, "b b b b b"));
        var gIsOne = new GameState();
        gIsOne.Globals["g"] = 1;

        Assert.Equal(1, PathStatsService.Analyze(snap, gIsOne, "poe2").ShadowedLinkCount);
        Assert.Equal(0, PathStatsService.Analyze(snap).ShadowedLinkCount);
        Assert.Equal(2, PathStatsService.Analyze(snap, gIsOne, "poe2").DefaultLongestWords);
    }

    [Fact]
    public void CrossConversation_HidingAppliesPerConversation()
    {
        // Node 1 of conversation A is OnceEver (never certain); node 1 of B is not. A link
        // from B must be judged against B's node, so B's second link is hidden.
        var a = Snap(
            Node(0, "s"),
            Node(1, "x") with { Persistence = "OnceEver" },
            Node(2, "y z w q"));
        var b = Snap(
            Node(0, "b", links: [Link(0, 1), Link(0, 2)]),
            Node(1, "p"),
            Node(2, "one two three four"));

        var report = PathStatsService.Analyze(Graph("A", Convs(("A", a), ("B", b)),
            new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))));

        Assert.Equal(1, report.ShadowedLinkCount);
        Assert.Equal(3, report.DefaultLongestWords);   // s + (b + p)
    }
}
