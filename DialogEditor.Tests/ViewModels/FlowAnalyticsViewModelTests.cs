using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class FlowAnalyticsViewModelTests
{
    public FlowAnalyticsViewModelTests() => Loc.Configure(new StubStringProvider());

    private static NodeEditSnapshot MakeNode(
        int id,
        SpeakerCategory category = SpeakerCategory.Npc,
        string defaultText = "",
        IReadOnlyList<LinkEditSnapshot>? links = null,
        bool isPlayerChoice = false) =>
        new(id, isPlayerChoice, category, "", "", defaultText, "",
            "Conversation", "None", "", "", "", false, false,
            links ?? [], [], []);

    private static LinkEditSnapshot Link(int from, int to) =>
        new(from, to, 1f, "", false);

    private static ConversationEditSnapshot SimpleSnapshot() => new([
        MakeNode(0, defaultText: "Hello", links: [Link(0, 1)]),
        MakeNode(1, defaultText: "World")
    ]);

    [Fact]
    public void InitialState_StatisticsIsNull_IssuesEmpty()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });

        Assert.Null(vm.Statistics);
        Assert.Empty(vm.Issues);
    }

    [Fact]
    public void Refresh_PopulatesStatisticsAndIssues()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.NotNull(vm.Statistics);
        Assert.Equal(2, vm.Statistics!.TotalNodes);
    }

    [Fact]
    public void Refresh_NoIssues_IssuesEmpty()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.Empty(vm.Issues);
    }

    [Fact]
    public void Refresh_WithIssues_PopulatesIssueViewModels()
    {
        // Node 2 is unreachable; nodes 0 and 1 have text so only node 2 has issues
        var snapshot = new ConversationEditSnapshot([
            MakeNode(0, defaultText: "Hello", links: [Link(0, 1)]),
            MakeNode(1, defaultText: "World"),
            MakeNode(2, defaultText: "Unreachable")
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.NotEmpty(vm.Issues);
        var unreachable = vm.Issues.First(i => i.Kind == FlowIssueKind.Unreachable);
        Assert.Equal(2, unreachable.NodeId);
    }

    [Fact]
    public void Refresh_NullSnapshot_DoesNotCrash()
    {
        var vm = new FlowAnalyticsViewModel(() => null, _ => { });

        vm.RefreshCommand.Execute(null); // should not throw
    }

    [Fact]
    public void Navigate_CallsCallbackWithCorrectNodeId()
    {
        var navigatedId = -1;
        var snapshot = new ConversationEditSnapshot([
            MakeNode(0, defaultText: "Hello", links: [Link(0, 1)]),
            MakeNode(1, defaultText: "World"),
            MakeNode(2, defaultText: "Unreachable")  // unreachable
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, id => navigatedId = id);
        vm.RefreshCommand.Execute(null);

        vm.Issues[0].NavigateCommand.Execute(null);

        Assert.Equal(2, navigatedId);
    }

    // ── Severity tier as text (2026-07-04) ───────────────────────────────

    [Theory]
    [InlineData(FlowIssueKind.Unreachable,              "FlowAnalytics_Severity_Error")]
    [InlineData(FlowIssueKind.PlayerDeadEnd,            "FlowAnalytics_Severity_Warning")]
    [InlineData(FlowIssueKind.EmptyText,                "FlowAnalytics_Severity_Warning")]
    [InlineData(FlowIssueKind.NoIncomingLinks,          "FlowAnalytics_Severity_Warning")]
    [InlineData(FlowIssueKind.BarkTextTooLong,          "FlowAnalytics_Severity_Warning")]
    [InlineData(FlowIssueKind.BarkHasPlayerChoiceChild, "FlowAnalytics_Severity_Warning")]
    public void SeverityLabel_MapsKindToTierKey(FlowIssueKind kind, string expectedKey)
    {
        var vm = new FlowIssueViewModel(new FlowIssue(1, kind), "snippet", _ => { });
        Assert.Equal(expectedKey, vm.SeverityLabel);   // StubStringProvider echoes keys
    }

    // ── Row text is localised (CLAUDE.md localisation rule) ─────────

    // The row label used to be built inline as $"Node {NodeId} — {NodeSnippet}",
    // which hard-codes the English word "Node" in C#. NoHardcodedUiStringsTests
    // only scans .axaml, so nothing caught it. StubStringProvider echoes keys, so
    // asserting the bare key proves the VM asks the resource layer for the text
    // instead of composing it — the end-to-end test below pins the actual wording.
    [Fact]
    public void FlowIssue_DisplayText_ComesFromResourceKey()
    {
        var vm = new FlowIssueViewModel(
            new FlowIssue(7, FlowIssueKind.EmptyText), "snippet", _ => { });

        Assert.Equal("FlowAnalytics_NodeLabel", vm.DisplayText);
    }

    [Fact]
    public void TokenIssueRow_DisplayText_ComesFromResourceKey()
    {
        var withLanguage = new TokenIssueRowViewModel(7, "de", "bad tag", _ => { });
        var defaultText  = new TokenIssueRowViewModel(7, "",   "bad tag", _ => { });

        Assert.Equal("FlowAnalytics_TagIssue_Row",         withLanguage.DisplayText);
        Assert.Equal("FlowAnalytics_TagIssue_Row_Default", defaultText.DisplayText);
    }

    // ── Playthrough stats ────────────────────────────────────────────────

    private static NodeEditSnapshot PathNode(
        int id, string defaultText = "", string femaleText = "",
        bool isPlayerChoice = false, string speaker = "",
        SpeakerCategory category = SpeakerCategory.Npc,
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, isPlayerChoice, category, speaker, "", defaultText, femaleText,
            "Conversation", "None", "", "", "", false, false, links ?? [], [], []);

    [Fact]
    public void Refresh_PopulatesBranchesAndSpeakers()
    {
        var snapshot = new ConversationEditSnapshot([
            PathNode(0, "start", speaker: "npc1", links: [Link(0, 1)]),
            PathNode(1, "reply", isPlayerChoice: true, speaker: "player",
                     category: SpeakerCategory.Player, links: [Link(1, 2)]),
            PathNode(2, "aa bb cc", speaker: "npc1")
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.True(vm.HasPathStats);
        Assert.Single(vm.Branches);
        Assert.NotEmpty(vm.WordsPerSpeaker);
    }

    [Fact]
    public void Refresh_FemaleColumns_GatedBySignificance()
    {
        var significant = new ConversationEditSnapshot([
            PathNode(0, "a", links: [Link(0, 1)]),
            PathNode(1, "b", femaleText: "one two three four five", isPlayerChoice: true)
        ]);
        var vm = new FlowAnalyticsViewModel(() => significant, _ => { });
        vm.RefreshCommand.Execute(null);
        Assert.True(vm.HasSignificantFemaleVariant);

        var plain = new ConversationEditSnapshot([
            PathNode(0, "a", links: [Link(0, 1)]),
            PathNode(1, "b", isPlayerChoice: true)
        ]);
        var vm2 = new FlowAnalyticsViewModel(() => plain, _ => { });
        vm2.RefreshCommand.Execute(null);
        Assert.False(vm2.HasSignificantFemaleVariant);
    }

    [Fact]
    public void BranchRow_Navigate_CallsCallbackWithChoiceNode()
    {
        var navigatedId = -1;
        var snapshot = new ConversationEditSnapshot([
            PathNode(0, "start", links: [Link(0, 7)]),
            PathNode(7, "reply", isPlayerChoice: true)
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, id => navigatedId = id);
        vm.RefreshCommand.Execute(null);

        vm.Branches[0].NavigateCommand.Execute(null);

        Assert.Equal(7, navigatedId);
    }

    // ── Configurable reading speed (issue #14) ───────────────────────────────

    [Fact] // Omitting the ctor argument keeps the historical 200 wpm.
    public void WordsPerMinute_DefaultsToPathStatsDefault()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });

        Assert.Equal(PathStatsFormat.DefaultWordsPerMinute, vm.WordsPerMinute);
    }

    [Fact] // The View supplies the persisted value at construction.
    public void WordsPerMinute_InitialisedFromConstructor()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { },
                                            wordsPerMinute: 120);

        Assert.Equal(120, vm.WordsPerMinute);
    }

    [Fact] // The preset list the ComboBox binds to must offer the default.
    public void WordsPerMinuteOptions_IncludeTheDefault()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });

        Assert.Contains(PathStatsFormat.DefaultWordsPerMinute, vm.WordsPerMinuteOptions);
        Assert.All(vm.WordsPerMinuteOptions, wpm => Assert.True(wpm > 0));
    }

    /// The branch/header strings are built once into plain strings, so a speed change
    /// only reaches the UI by re-running the analysis. Assert the re-run rather than the
    /// rendered text: the test string provider echoes keys, so the formatted m:ss value
    /// never appears in the output here (PathStatsFormatTests covers the arithmetic).
    [Fact]
    public void WordsPerMinute_Change_ReRunsAnalysis()
    {
        var snapshotReads = 0;
        var vm = new FlowAnalyticsViewModel(
            () => { snapshotReads++; return SimpleSnapshot(); }, _ => { });
        vm.RefreshCommand.Execute(null);
        var before = snapshotReads;

        vm.WordsPerMinute = 120;

        Assert.True(snapshotReads > before,
            "changing the reading speed should re-run Refresh so the m:ss figures update");
    }

    [Fact] // Persistence is the View's job; the VM just reports the change.
    public void WordsPerMinute_Change_InvokesPersistCallback()
    {
        var persisted = 0;
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { },
                                            persistWordsPerMinute: v => persisted = v);

        vm.WordsPerMinute = 300;

        Assert.Equal(300, persisted);
    }

    [Fact] // No callback wired (the unit-test default) must not throw.
    public void WordsPerMinute_Change_WithoutPersistCallback_DoesNotThrow()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });

        vm.WordsPerMinute = 150;

        Assert.Equal(150, vm.WordsPerMinute);
    }

    // ── Recursive fork tree + endings (issue #14) ────────────────────────────

    /// root -> A (choice) -> npc -> A1 (choice). A1 is a fork UNDER A, never a sibling.
    private static ConversationEditSnapshot NestedForkSnapshot() => new([
        PathNode(0, "start", links: [Link(0, 1)]),
        PathNode(1, "A", isPlayerChoice: true, links: [Link(1, 2)]),
        PathNode(2, "npc", links: [Link(2, 3)]),
        PathNode(3, "A1", isPlayerChoice: true)
    ]);

    [Fact]
    public void Refresh_NestsSubForksUnderTheirOpeningChoice()
    {
        var vm = new FlowAnalyticsViewModel(() => NestedForkSnapshot(), _ => { });

        vm.RefreshCommand.Execute(null);

        var a = Assert.Single(vm.Branches);
        Assert.Equal("A", a.ChoiceText);
        var a1 = Assert.Single(a.SubBranches);
        Assert.Equal("A1", a1.ChoiceText);
        Assert.Empty(a1.SubBranches);
    }

    [Fact] // Top level opens; deeper levels stay folded so the panel is readable.
    public void Refresh_TopLevelForksExpanded_DeeperOnesCollapsed()
    {
        var vm = new FlowAnalyticsViewModel(() => NestedForkSnapshot(), _ => { });

        vm.RefreshCommand.Execute(null);

        var a = vm.Branches[0];
        Assert.True(a.HasSubBranches);
        Assert.True(a.IsExpanded);
        Assert.False(a.SubBranches[0].IsExpanded);
        Assert.False(a.SubBranches[0].HasSubBranches);
    }

    [Fact]
    public void SubForkRow_Navigate_CallsCallbackWithItsOwnNode()
    {
        var navigatedId = -1;
        var vm = new FlowAnalyticsViewModel(() => NestedForkSnapshot(), id => navigatedId = id);
        vm.RefreshCommand.Execute(null);

        vm.Branches[0].SubBranches[0].NavigateCommand.Execute(null);

        Assert.Equal(3, navigatedId);
    }

    [Fact]
    public void Refresh_PopulatesEndings_LongestFirst()
    {
        var snapshot = new ConversationEditSnapshot([
            PathNode(0, "start", links: [Link(0, 1), Link(0, 2)]),
            PathNode(1, "A", isPlayerChoice: true, links: [Link(1, 3)]),
            PathNode(3, "the long ending"),
            PathNode(2, "B", isPlayerChoice: true)
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.True(vm.HasEndings);
        Assert.Equal([3, 2], vm.Endings.Select(e => e.NodeId));
    }

    [Fact]
    public void EndingRow_Navigate_CallsCallbackWithEndingNode()
    {
        var navigatedId = -1;
        var snapshot = new ConversationEditSnapshot([
            PathNode(0, "start", links: [Link(0, 4)]),
            PathNode(4, "the end")
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, id => navigatedId = id);
        vm.RefreshCommand.Execute(null);

        vm.Endings[0].NavigateCommand.Execute(null);

        Assert.Equal(4, navigatedId);
    }

    [Fact] // Every exit loops backwards: a DAG terminal, but not a way the talk ends.
    public void Refresh_LoopOnlyConversation_HasNoEndings()
    {
        var snapshot = new ConversationEditSnapshot([
            PathNode(0, "a", links: [Link(0, 1)]),
            PathNode(1, "b", links: [Link(1, 0)])
        ]);
        var vm = new FlowAnalyticsViewModel(() => snapshot, _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.False(vm.HasEndings);
        Assert.Empty(vm.Endings);
    }

    // ── Cross-conversation handoffs (#14) ────────────────────────────────────

    private static MultiConversationGraph EmptyGraph() =>
        new("", new Dictionary<string, ConversationEditSnapshot> { [""] = SimpleSnapshot() },
            [], []);

    /// Root conversation A hands off to B, whose entry node leads to a player choice —
    /// so the fork tree contains a row in another conversation.
    private static MultiConversationGraph GraphWithForkInB()
    {
        var a = new ConversationEditSnapshot([MakeNode(0, defaultText: "greeting")]);
        var b = new ConversationEditSnapshot([
            MakeNode(0, defaultText: "hub", links: [Link(0, 1)]),
            MakeNode(1, defaultText: "pick me", isPlayerChoice: true)]);
        return new MultiConversationGraph("A",
            new Dictionary<string, ConversationEditSnapshot> { ["A"] = a, ["B"] = b },
            [new JumpEdge(new NodeRef("A", 0), new NodeRef("B", 0))],
            []);
    }

    [Fact]
    public void ToggleOff_UsesTheSnapshotOverload()
    {
        var called = false;
        var vm = new FlowAnalyticsViewModel(
            () => SimpleSnapshot(), _ => { },
            resolveGraph: () => { called = true; return EmptyGraph(); },
            followConversationJumps: false);

        vm.RefreshCommand.Execute(null);

        Assert.False(called);
    }

    [Fact]
    public void ToggleOn_UsesTheResolver()
    {
        var called = false;
        var vm = new FlowAnalyticsViewModel(
            () => SimpleSnapshot(), _ => { },
            resolveGraph: () => { called = true; return EmptyGraph(); },
            followConversationJumps: true);

        vm.RefreshCommand.Execute(null);

        Assert.True(called);
    }

    [Fact]
    public void TogglingPersistsAndRefreshes()
    {
        bool? persisted = null;
        var vm = new FlowAnalyticsViewModel(
            () => SimpleSnapshot(), _ => { },
            resolveGraph: EmptyGraph,
            persistFollowJumps: v => persisted = v);

        vm.FollowConversationJumps = true;

        Assert.True(persisted);
        Assert.True(vm.HasPathStats);   // the refresh actually ran
    }

    [Fact]
    public void ToggleOn_WithNoResolver_FallsBackSafely()
    {
        var vm = new FlowAnalyticsViewModel(
            () => SimpleSnapshot(), _ => { },
            followConversationJumps: true);       // resolveGraph is null

        vm.RefreshCommand.Execute(null);          // must not throw

        Assert.True(vm.HasPathStats);
    }

    [Fact]
    public void CrossConversationRow_NavigatesThroughTheTwoArgumentDelegate()
    {
        (string Conv, int Node)? went = null;
        var vm = new FlowAnalyticsViewModel(
            () => SimpleSnapshot(),
            _ => Assert.Fail("should use the cross-conversation delegate"),
            resolveGraph: GraphWithForkInB,
            followConversationJumps: true,
            navigateToNodeInConversation: (c, n) => went = (c, n));

        vm.RefreshCommand.Execute(null);
        var row = vm.Branches.Single(b => b.IsInAnotherConversation);
        row.NavigateCommand.Execute(null);

        Assert.Equal(("B", 1), went);
    }

    [Fact]
    public void SameConversationRow_HasNoConversationTag()
    {
        var vm = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });

        vm.RefreshCommand.Execute(null);

        Assert.All(vm.Endings, e => Assert.False(e.IsInAnotherConversation));
    }

    [Fact]
    public void ConversationsSpanned_ShownOnlyWhenMoreThanOne()
    {
        var single = new FlowAnalyticsViewModel(() => SimpleSnapshot(), _ => { });
        single.RefreshCommand.Execute(null);
        Assert.False(single.HasSpannedConversations);

        var multi = new FlowAnalyticsViewModel(
            () => SimpleSnapshot(), _ => { },
            resolveGraph: GraphWithForkInB, followConversationJumps: true);
        multi.RefreshCommand.Execute(null);
        Assert.True(multi.HasSpannedConversations);
    }

    [Fact]
    public void UnresolvedHandoff_GetsTheLocalisedPlaceholderLabel()
    {
        var graph = new MultiConversationGraph("A",
            new Dictionary<string, ConversationEditSnapshot> { ["A"] = SimpleSnapshot() },
            [],
            [new UnfollowedJump(new NodeRef("A", 0), "", UnfollowedReason.Unresolved)]);

        var vm = new FlowAnalyticsViewModel(
            () => SimpleSnapshot(), _ => { },
            resolveGraph: () => graph, followConversationJumps: true);

        vm.RefreshCommand.Execute(null);

        var row = Assert.Single(vm.UnfollowedJumpRows);
        Assert.False(string.IsNullOrEmpty(row.TargetLabel));   // resolver left it empty
        Assert.False(string.IsNullOrEmpty(row.ReasonText));
    }
}
