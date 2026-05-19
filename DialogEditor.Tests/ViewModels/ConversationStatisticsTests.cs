using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class ConversationStatisticsTests
{
    public ConversationStatisticsTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel MakeNode(int id, bool isPlayer, string text, string femaleText = "")
    {
        var node = new ConversationNode(id, isPlayer, SpeakerCategory.Npc,
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

    [Fact]
    public void Statistics_NodeCounts_Correct()
    {
        var canvas = MakeCanvas(
            MakeNode(1, false, "Hello"),
            MakeNode(2, true, "Goodbye"),
            MakeNode(3, false, "Indeed"));

        var s = canvas.Statistics;
        Assert.Equal(3, s.NodeCount);
        Assert.Equal(2, s.NpcCount);
        Assert.Equal(1, s.PlayerCount);
    }

    [Fact]
    public void Statistics_WordCount_SumsDefaultText()
    {
        var canvas = MakeCanvas(
            MakeNode(1, false, "Hello world"),
            MakeNode(2, false, "One two three"));

        Assert.Equal(5, canvas.Statistics.WordCount);
    }

    [Fact]
    public void Statistics_WordCount_EmptyText_IsZero()
    {
        var canvas = MakeCanvas(MakeNode(1, false, ""));
        Assert.Equal(0, canvas.Statistics.WordCount);
    }

    [Fact]
    public void Statistics_FemaleWordCount_OnlyCountsNonEmpty()
    {
        var canvas = MakeCanvas(
            MakeNode(1, false, "Male",  "Female text here"),
            MakeNode(2, false, "Other", ""));

        Assert.Equal(3, canvas.Statistics.FemaleWordCount);
    }

    [Fact]
    public void Statistics_UpdatesWhenNodeAdded()
    {
        var canvas = MakeCanvas(MakeNode(1, false, "Hello"));
        Assert.Equal(1, canvas.Statistics.NodeCount);

        canvas.Nodes.Add(MakeNode(2, false, "World"));
        Assert.Equal(2, canvas.Statistics.NodeCount);
    }
}
