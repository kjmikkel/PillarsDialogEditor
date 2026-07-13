using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class ProjectFindViewModelTests
{
    private const string SpeakerGuid = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";

    public ProjectFindViewModelTests()
    {
        Loc.Configure(new StubStringProvider());
    }

    [Fact]
    public void Search_PopulatesResults_AndStatus()
    {
        var (project, provider) = ProjectWith("c1", 1, "The Watcher");
        var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null))
        { SearchText = "Watcher" };
        vm.SearchCommand.Execute(null);
        Assert.Single(vm.Results);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public void Search_EmptyText_CommandDisabled()
    {
        var (project, provider) = ProjectWith("c1", 1, "x");
        var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null));
        Assert.False(vm.SearchCommand.CanExecute(null));
        vm.SearchText = "x";
        Assert.True(vm.SearchCommand.CanExecute(null));
    }

    [Fact]
    public void Toggles_FlowIntoQuery()
    {
        var (project, provider) = ProjectWithLink("c1", 1, "Ask about X");
        var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null))
        { SearchText = "Ask about X" };
        vm.SearchCommand.Execute(null);
        Assert.Empty(vm.Results);                 // link off
        vm.InLinkChoice = true;
        vm.SearchCommand.Execute(null);
        Assert.Single(vm.Results);
    }

    [Fact]
    public void NavigateTo_RaisesRequestNavigate_WithTarget()
    {
        var (project, provider) = ProjectWith("c1", 7, "The Watcher");
        var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null))
        { SearchText = "Watcher" };
        vm.SearchCommand.Execute(null);
        (string, int)? got = null;
        vm.RequestNavigate += (c, n) => got = (c, n);
        vm.NavigateTo(vm.Results[0]);
        Assert.Equal(("c1", 7), got);
    }

    // -- helpers (mirrors DialogEditor.Tests.Services.ProjectFindServiceTests) --

    private static ConversationNode MakeNode(int id, IReadOnlyList<NodeLink>? links = null) =>
        new(id, false, SpeakerCategory.Npc, SpeakerGuid, "", links ?? [], [], [], "Conversation", "None");

    private static ConversationPatch EmptyPatch(string convName) =>
        new(convName, ConversationPatch.CurrentSchemaVersion, [], [], []);

    private static (DialogProject, IGameDataProvider) ProjectWith(string conv, int nodeId, string defaultText)
    {
        var node = MakeNode(nodeId);
        var strings = new StringTable([new StringEntry(nodeId, defaultText, "")]);
        var convObj = new Conversation(conv, [node], strings);
        var provider = new FakeGameDataProvider("poe2", "en", convObj);
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch(conv));
        return (project, provider);
    }

    private static (DialogProject, IGameDataProvider) ProjectWithLink(string conv, int nodeId, string linkText)
    {
        var link = new NodeLink(nodeId, nodeId + 1, [], 1f, linkText);
        var node = MakeNode(nodeId, [link]);
        var strings = new StringTable([new StringEntry(nodeId, "unrelated text", "")]);
        var convObj = new Conversation(conv, [node], strings);
        var provider = new FakeGameDataProvider("poe2", "en", convObj);
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch(conv));
        return (project, provider);
    }
}
