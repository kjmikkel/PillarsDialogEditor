using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Tests.Serialization;

public class StringTableSerializerTests
{
    private static NodeEditSnapshot Node(int id, string def, string fem = "") =>
        new(id, false, SpeakerCategory.Npc, "", "", def, fem,
            "Conversation", "None", "", "", "", false, false, []);

    private const string TwoEntryXml = """
        <StringTableFile>
          <Entries>
            <Entry><ID>0</ID><DefaultText>Hello</DefaultText><FemaleText></FemaleText></Entry>
            <Entry><ID>1</ID><DefaultText>Goodbye</DefaultText><FemaleText>Farewell</FemaleText></Entry>
          </Entries>
        </StringTableFile>
        """;

    [Fact]
    public void Serialize_UpdatesExistingEntry()
    {
        var nodes = new[] { Node(0, "Hi"), Node(1, "Goodbye", "Farewell") };
        var result = StringTableSerializer.Serialize(TwoEntryXml, nodes);
        var reparsed = StringTableParser.Parse(result);
        Assert.Equal("Hi", reparsed.Get(0)!.DefaultText);
        Assert.Equal("Goodbye", reparsed.Get(1)!.DefaultText);
    }

    [Fact]
    public void Serialize_AddsNewEntry()
    {
        var nodes = new[] { Node(0, "Hello"), Node(99, "New node text") };
        var result = StringTableSerializer.Serialize(TwoEntryXml, nodes);
        var reparsed = StringTableParser.Parse(result);
        Assert.Equal("New node text", reparsed.Get(99)!.DefaultText);
    }

    [Fact]
    public void Serialize_PreservesEntriesNotInSnapshot()
    {
        var nodes = new[] { Node(0, "Hello") };
        var result = StringTableSerializer.Serialize(TwoEntryXml, nodes);
        var reparsed = StringTableParser.Parse(result);
        Assert.Equal("Goodbye", reparsed.Get(1)!.DefaultText);
    }

    [Fact]
    public void Serialize_WritesFemaleText()
    {
        var nodes = new[] { Node(0, "Hello", "Greetings ladies") };
        var result = StringTableSerializer.Serialize(TwoEntryXml, nodes);
        var reparsed = StringTableParser.Parse(result);
        Assert.Equal("Greetings ladies", reparsed.Get(0)!.FemaleText);
    }

    [Fact]
    public void Serialize_EmptyOriginal_CreatesValidDocument()
    {
        var nodes = new[] { Node(5, "Brand new") };
        var result = StringTableSerializer.Serialize(string.Empty, nodes);
        var reparsed = StringTableParser.Parse(result);
        Assert.Equal("Brand new", reparsed.Get(5)!.DefaultText);
    }
}
