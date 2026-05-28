using DialogEditor.Core.Editing;
using DialogEditor.Core.Export;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Export;

public class YarnSpinnerExporterTests
{
    private static NodeEditSnapshot MakeNode(
        int id, SpeakerCategory category, string defaultText,
        List<LinkEditSnapshot>? links = null) =>
        new(
            NodeId: id, IsPlayerChoice: category == SpeakerCategory.Player,
            SpeakerCategory: category, SpeakerGuid: "", ListenerGuid: "",
            DefaultText: defaultText, FemaleText: "",
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "", ExternalVO: "",
            HasVO: false, HideSpeaker: false,
            Links: links ?? [], Conditions: [], Scripts: []);

    private static LinkEditSnapshot MakeLink(int from, int to) =>
        new(FromNodeId: from, ToNodeId: to, RandomWeight: 1f,
            QuestionNodeTextDisplay: "", HasConditions: false)
        { Conditions = null };

    [Fact]
    public void Export_NpcNode_WritesTitleAndSpeakerLine()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hello there."),
        };
        var path = Path.GetTempFileName() + ".yarn";
        try
        {
            new YarnSpinnerExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("title: 1",            content);
            Assert.Contains("---",                 content);
            Assert.Contains("Npc: Hello there.",   content);
            Assert.Contains("===",                 content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_PlayerChoiceNode_WritesChoiceLineWithTarget()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(2, SpeakerCategory.Player, "I need work.", [MakeLink(2, 3)]),
        };
        var path = Path.GetTempFileName() + ".yarn";
        try
        {
            new YarnSpinnerExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("title: 2",              content);
            Assert.Contains("-> I need work. [[3]]", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_PlayerChoiceWithNoLinks_WritesChoiceLineWithoutTarget()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(5, SpeakerCategory.Player, "Farewell."),
        };
        var path = Path.GetTempFileName() + ".yarn";
        try
        {
            new YarnSpinnerExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("-> Farewell.", content);
            Assert.DoesNotContain("[[", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_MultipleNodes_AllGetTitleBlocks()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc,    "Hello.",       [MakeLink(1, 2)]),
            MakeNode(2, SpeakerCategory.Player, "I need work.", [MakeLink(2, 3)]),
            MakeNode(3, SpeakerCategory.Npc,    "Here's work."),
        };
        var path = Path.GetTempFileName() + ".yarn";
        try
        {
            new YarnSpinnerExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("title: 1", content);
            Assert.Contains("title: 2", content);
            Assert.Contains("title: 3", content);
        }
        finally { File.Delete(path); }
    }
}
