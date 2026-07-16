using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.ViewModels;

public class NodeSearchStateTests
{
    public NodeSearchStateTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void NewNode_DefaultsToNone()
    {
        var node = new ConversationNode(0, false, SpeakerCategory.Npc, "", "", [], [], [], "", "");
        var vm = new NodeViewModel(node, null);
        Assert.Equal(SearchMatchState.None, vm.SearchMatchState);
    }
}
