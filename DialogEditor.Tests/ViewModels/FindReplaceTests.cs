using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class FindReplaceTests
{
    public FindReplaceTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel MakeNode(int id, string text, string femaleText = "")
    {
        var node = new ConversationNode(id, false, SpeakerCategory.Npc,
            "", "", [], [], [], "Conversation", "None");
        return new NodeViewModel(node, new StringEntry(id, text, femaleText));
    }

    private static ConversationViewModel MakeCanvas(params NodeViewModel[] nodes)
    {
        var vm = new ConversationViewModel(new StubDispatcher());
        foreach (var n in nodes)
            vm.Nodes.Add(n);
        return vm;
    }

    // ── Find ──────────────────────────────────────────────────────────────

    [Fact]
    public void Find_MatchingText_ReturnsResults()
    {
        var canvas = MakeCanvas(MakeNode(1, "Hello world"), MakeNode(2, "Goodbye"));
        var vm     = new FindReplaceViewModel(canvas);
        vm.SearchText = "Hello";
        vm.FindCommand.Execute(null);
        Assert.Single(vm.Results);
        Assert.Equal(1, vm.Results[0].Node.NodeId);
    }

    [Fact]
    public void Find_NoMatch_ReturnsEmpty()
    {
        var canvas = MakeCanvas(MakeNode(1, "Hello world"));
        var vm     = new FindReplaceViewModel(canvas);
        vm.SearchText = "xyz";
        vm.FindCommand.Execute(null);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public void Find_CaseInsensitive_ByDefault()
    {
        var canvas = MakeCanvas(MakeNode(1, "Hello World"));
        var vm     = new FindReplaceViewModel(canvas);
        vm.SearchText = "hello";
        vm.FindCommand.Execute(null);
        Assert.Single(vm.Results);
    }

    [Fact]
    public void Find_CaseSensitive_NoMatchOnWrongCase()
    {
        var canvas = MakeCanvas(MakeNode(1, "Hello World"));
        var vm     = new FindReplaceViewModel(canvas);
        vm.SearchText    = "hello";
        vm.CaseSensitive = true;
        vm.FindCommand.Execute(null);
        Assert.Empty(vm.Results);
    }

    [Fact]
    public void Find_SearchesFemaleText()
    {
        var canvas = MakeCanvas(MakeNode(1, "Male text", "Female text"));
        var vm     = new FindReplaceViewModel(canvas);
        vm.SearchText = "Female";
        vm.FindCommand.Execute(null);
        Assert.Single(vm.Results);
        Assert.Equal("FemaleText", vm.Results[0].FieldName);
    }

    // ── Replace ───────────────────────────────────────────────────────────

    [Fact]
    public void ReplaceAll_UpdatesMatchingNodes()
    {
        var node   = MakeNode(1, "Hello world");
        var canvas = MakeCanvas(node);
        var vm     = new FindReplaceViewModel(canvas);
        vm.SearchText  = "Hello";
        vm.ReplaceText = "Hi";
        vm.FindCommand.Execute(null);
        vm.ReplaceAllCommand.Execute(null);
        Assert.Equal("Hi world", node.DefaultText);
    }

    [Fact]
    public void ReplaceAll_ReplacesInFemaleText()
    {
        var node   = MakeNode(1, "Male", "Female text");
        var canvas = MakeCanvas(node);
        var vm     = new FindReplaceViewModel(canvas);
        vm.SearchText  = "Female";
        vm.ReplaceText = "Her";
        vm.FindCommand.Execute(null);
        vm.ReplaceAllCommand.Execute(null);
        Assert.Equal("Her text", node.FemaleText);
    }

    [Fact]
    public void ReplaceAll_MultipleOccurrencesInSameNode_AllReplaced()
    {
        var node   = MakeNode(1, "cat and cat");
        var canvas = MakeCanvas(node);
        var vm     = new FindReplaceViewModel(canvas);
        vm.SearchText  = "cat";
        vm.ReplaceText = "dog";
        vm.FindCommand.Execute(null);
        vm.ReplaceAllCommand.Execute(null);
        Assert.Equal("dog and dog", node.DefaultText);
    }
}
