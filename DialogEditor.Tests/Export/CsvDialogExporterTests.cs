using DialogEditor.Core.Editing;
using DialogEditor.Core.Export;
using DialogEditor.Core.Import;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Export;

public class CsvDialogExporterTests
{
    private static NodeEditSnapshot MakeNode(
        int id,
        SpeakerCategory category,
        string defaultText,
        string femaleText = "",
        List<LinkEditSnapshot>? links = null) =>
        new(
            NodeId: id,
            IsPlayerChoice: category == SpeakerCategory.Player,
            SpeakerCategory: category,
            SpeakerGuid: "",
            ListenerGuid: "",
            DefaultText: defaultText,
            FemaleText: femaleText,
            DisplayType: "Conversation",
            Persistence: "None",
            ActorDirection: "",
            Comments: "",
            ExternalVO: "",
            HasVO: false,
            HideSpeaker: false,
            Links: links ?? [],
            Conditions: [],
            Scripts: []);

    private static LinkEditSnapshot MakeLink(int from, int to) =>
        new(FromNodeId: from, ToNodeId: to, RandomWeight: 1f,
            QuestionNodeTextDisplay: "", HasConditions: false)
        { Conditions = null };

    [Fact]
    public void Export_ThenImport_RoundTripsNodeCountLinksAndSpeakerCategory()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hello.", links: [MakeLink(1, 2), MakeLink(1, 3)]),
            MakeNode(2, SpeakerCategory.Player, "Go left.", links: [MakeLink(2, 4)]),
            MakeNode(3, SpeakerCategory.Player, "Go right.", links: [MakeLink(3, 4)]),
        };
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            new CsvDialogExporter().Export(new ConversationExport("test", nodes), path);
            var imported = new CsvDialogImporter().Import(path);

            Assert.Equal(3, imported.Nodes.Count);
            Assert.Equal(SpeakerCategory.Npc,    imported.Nodes[0].SpeakerCategory);
            Assert.Equal(SpeakerCategory.Player, imported.Nodes[1].SpeakerCategory);
            Assert.Equal(2, imported.Nodes[0].Links.Count);
            Assert.Equal(2, imported.Nodes[0].Links[0].ToNodeId);
            Assert.Equal(3, imported.Nodes[0].Links[1].ToNodeId);
            Assert.Equal(4, imported.Nodes[1].Links[0].ToNodeId);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_FieldWithComma_IsQuoted()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hello, world."),
        };
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            new CsvDialogExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("\"Hello, world.\"", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_FieldWithQuote_IsDoubledAndWrapped()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "She said \"hello\"."),
        };
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            new CsvDialogExporter().Export(new ConversationExport("test", nodes), path);
            var content = File.ReadAllText(path);
            Assert.Contains("\"She said \"\"hello\"\".\"", content);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Export_FemaleText_RoundTrips()
    {
        var nodes = new List<NodeEditSnapshot>
        {
            MakeNode(1, SpeakerCategory.Npc, "Hello.", femaleText: "Greetings."),
        };
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            new CsvDialogExporter().Export(new ConversationExport("test", nodes), path);
            var imported = new CsvDialogImporter().Import(path);
            Assert.Equal("Greetings.", imported.Nodes[0].FemaleText);
        }
        finally { File.Delete(path); }
    }
}
