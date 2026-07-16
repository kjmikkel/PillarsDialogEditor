using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class ConversationHighlightTests
{
    public ConversationHighlightTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel MakeNode(int id)
    {
        var node = new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None");
        return new NodeViewModel(node, new StringEntry(id, "text", ""));
    }

    private static ConversationViewModel MakeCanvas(params NodeViewModel[] nodes)
    {
        var vm = new ConversationViewModel(new StubDispatcher());
        foreach (var n in nodes) vm.Nodes.Add(n);
        return vm;
    }

    [Fact]
    public void ApplyConditionHighlight_MatchesGetMatch_RestGetDimmed()
    {
        var n0 = MakeNode(0);
        var n1 = MakeNode(1);
        var canvas = MakeCanvas(n0, n1);

        canvas.ApplyConditionHighlight(new HashSet<int> { 0 });

        Assert.Equal(SearchMatchState.Match, n0.SearchMatchState);
        Assert.Equal(SearchMatchState.Dimmed, n1.SearchMatchState);
    }

    [Fact]
    public void ClearConditionHighlight_ResetsAllToNone()
    {
        var n0 = MakeNode(0);
        var n1 = MakeNode(1);
        var canvas = MakeCanvas(n0, n1);
        canvas.ApplyConditionHighlight(new HashSet<int> { 0 });

        canvas.ClearConditionHighlight();

        Assert.Equal(SearchMatchState.None, n0.SearchMatchState);
        Assert.Equal(SearchMatchState.None, n1.SearchMatchState);
    }
}
