using DialogEditor.Core.Editing;
using DialogEditor.Core.Export;
using DialogEditor.Core.Import;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Export;

public class JsonDialogExporterTests
{
    private static NodeEditSnapshot MakeNode(
        int id, SpeakerCategory category, string defaultText,
        string femaleText = "", List<LinkEditSnapshot>? links = null) =>
        new(
            NodeId: id, IsPlayerChoice: category == SpeakerCategory.Player,
            SpeakerCategory: category, SpeakerGuid: "", ListenerGuid: "",
            DefaultText: defaultText, FemaleText: femaleText,
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "", ExternalVO: "",
            HasVO: false, HideSpeaker: false,
            Links: links ?? [], Conditions: [], Scripts: []);

    private static LinkEditSnapshot MakeLink(int from, int to) =>
        new(FromNodeId: from, ToNodeId: to, RandomWeight: 1f,
            QuestionNodeTextDisplay: "", HasConditions: false)
        { Conditions = null };

    [Fact]
    public void Export_ThenImport_RoundTripsAllFields()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hello.", "Greetings.",
                [MakeLink(1, 2), MakeLink(1, 3)]),
            MakeNode(2, SpeakerCategory.Player, "Go left.", links: [MakeLink(2, 4)]),
            MakeNode(3, SpeakerCategory.Player, "Go right.", links: [MakeLink(3, 4)]),
        };
        var path = Path.GetTempFileName() + ".json";
        try
        {
            new JsonDialogExporter().Export(new ConversationExport("my_conv", nodes), path);
            var imported = new JsonDialogImporter().Import(path);

            Assert.Equal("my_conv",            imported.SuggestedName);
            Assert.Equal(3,                    imported.Nodes.Count);
            Assert.Equal(SpeakerCategory.Npc,  imported.Nodes[0].SpeakerCategory);
            Assert.Equal("Hello.",             imported.Nodes[0].DefaultText);
            Assert.Equal("Greetings.",         imported.Nodes[0].FemaleText);
            Assert.Equal(2,                    imported.Nodes[0].Links.Count);
            Assert.Equal(2,                    imported.Nodes[0].Links[0].ToNodeId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_WritesConversationName()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hi."),
        };
        var path = Path.GetTempFileName() + ".json";
        try
        {
            new JsonDialogExporter().Export(new ConversationExport("city_market", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("\"name\"",       content);
            Assert.Contains("city_market",    content);
        }
        finally { File.Delete(path); }
    }
}
