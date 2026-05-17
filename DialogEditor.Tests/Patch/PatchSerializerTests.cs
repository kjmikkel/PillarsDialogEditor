using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class PatchSerializerTests
{
    private static ConversationPatch MakeRichPatch()
    {
        var addedNode = new NodeEditSnapshot(
            99, true, SpeakerCategory.Player, "spkr", "lstnr",
            "Added text", "Female text", "Bark", "OnceEver",
            "direction", "comment", "vo.wav", true, false,
            [new LinkEditSnapshot(99, 100, 1.5f, "Always", true)]);

        var mod = new NodeModification(7,
            new Dictionary<string, FieldChange>
            {
                ["DefaultText"] = new("\"old\"", "\"new\""),
                ["HasVO"]       = new("false", "true"),
            },
            [new LinkEditSnapshot(7, 8, 1f, "", false)],
            [new DeletedLink(9, false)]);

        return new ConversationPatch(
            "test_conversation",
            ConversationPatch.CurrentSchemaVersion,
            [addedNode],
            [15, 16],
            [mod]);
    }

    [Fact]
    public void Serialize_RoundTrip_PreservesAllFields()
    {
        var original   = MakeRichPatch();
        var json       = PatchSerializer.Serialize(original);
        var deserialized = PatchSerializer.Deserialize(json);

        Assert.Equal(original.ConversationName, deserialized.ConversationName);
        Assert.Equal(original.SchemaVersion,    deserialized.SchemaVersion);

        // Added nodes
        Assert.Single(deserialized.AddedNodes);
        var node = deserialized.AddedNodes[0];
        Assert.Equal(99,           node.NodeId);
        Assert.True(node.IsPlayerChoice);
        Assert.Equal("Added text", node.DefaultText);
        Assert.Equal("Female text", node.FemaleText);
        Assert.Single(node.Links);
        Assert.Equal(100,     node.Links[0].ToNodeId);
        Assert.Equal(1.5f,    node.Links[0].RandomWeight);
        Assert.Equal("Always",node.Links[0].QuestionNodeTextDisplay);

        // Deleted node IDs
        Assert.Equal([15, 16], deserialized.DeletedNodeIds);

        // Modified nodes
        Assert.Single(deserialized.ModifiedNodes);
        var m = deserialized.ModifiedNodes[0];
        Assert.Equal(7, m.NodeId);
        Assert.Equal("\"old\"", m.FieldChanges["DefaultText"].From);
        Assert.Equal("\"new\"", m.FieldChanges["DefaultText"].To);
        Assert.Equal("false",   m.FieldChanges["HasVO"].From);
        Assert.Equal("true",    m.FieldChanges["HasVO"].To);
        Assert.Single(m.AddedLinks);
        Assert.Equal(8,  m.AddedLinks[0].ToNodeId);
        Assert.Single(m.DeletedLinks);
        Assert.Equal(9,  m.DeletedLinks[0].ToNodeId);
    }

    [Fact]
    public void Serialize_ProducesHumanReadableJson()
    {
        var patch = new ConversationPatch("c", 1, [], [], []);
        var json  = PatchSerializer.Serialize(patch);
        Assert.Contains("\"ConversationName\"", json);
        Assert.Contains('\n', json); // indented
    }
}
