using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class FlowAnalyticsViewModelValidationTests
{
    public FlowAnalyticsViewModelValidationTests() => Loc.Configure(new StubStringProvider());

    // Mirrors FlowAnalyticsViewModelTests.MakeNode's NodeEditSnapshot(...) call.
    private static NodeEditSnapshot Node(int id, string defaultText) =>
        new(id, false, SpeakerCategory.Npc, "", "", defaultText, "",
            "Conversation", "None", "", "", "", false, false, [], [], []);

    private static ConversationEditSnapshot SnapshotWith(string defaultText) =>
        new([Node(0, defaultText)]);

    private static IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>> NoTranslations() =>
        new Dictionary<string, IReadOnlyList<NodeTranslation>>();

    [Fact]
    public void TokenIssues_BadTokenInDefaultText_Surfaced()
    {
        var vm = new FlowAnalyticsViewModel(
            () => SnapshotWith("Hi [Player Nmae]"), _ => { }, NoTranslations);
        vm.RefreshCommand.Execute(null);
        Assert.NotEmpty(vm.TokenIssues);
        Assert.Contains(vm.TokenIssues, r => r.NodeId == 0 && r.Language == "");
    }

    [Fact]
    public void TokenIssues_BadTokenInTranslation_Surfaced()
    {
        var translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
        {
            ["fr"] = new[] { new NodeTranslation(0, "Bonjour [Player Nmae]", "") }
        };
        var vm = new FlowAnalyticsViewModel(
            () => SnapshotWith("Hi [Player Name]"),   // default clean
            _ => { },
            () => translations);
        vm.RefreshCommand.Execute(null);
        Assert.Contains(vm.TokenIssues, r => r.NodeId == 0 && r.Language == "fr");
    }

    [Fact]
    public void TokenIssues_CleanConversation_IsEmpty()
    {
        var vm = new FlowAnalyticsViewModel(
            () => SnapshotWith("Hi [Player Name]"), _ => { }, NoTranslations);
        vm.RefreshCommand.Execute(null);
        Assert.Empty(vm.TokenIssues);
    }
}
