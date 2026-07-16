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
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, false, category, "", "", defaultText, "",
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
}
