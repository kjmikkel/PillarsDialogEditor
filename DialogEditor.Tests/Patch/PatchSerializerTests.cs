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
            [new LinkEditSnapshot(99, 100, 1.5f, "Always", true)], [], []);

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

    [Fact]
    public void Serialize_V2_TranslationsRoundTrip()
    {
        var patch = new ConversationPatch("conv", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "Hello", "")],
                ["fr"] = [new NodeTranslation(1, "Bonjour", "")],
            }
        };
        var json  = PatchSerializer.Serialize(patch);
        var back  = PatchSerializer.Deserialize(json);
        Assert.Equal(2, back.Translations.Count);
        Assert.Equal("Hello",   back.Translations["en"][0].DefaultText);
        Assert.Equal("Bonjour", back.Translations["fr"][0].DefaultText);
    }

    [Fact]
    public void Serialize_V2_NodeCommentsRoundTrip()
    {
        var patch = new ConversationPatch("conv", 2, [], [], [])
        {
            NodeComments = new Dictionary<int, string> { [42] = "Said sarcastically." }
        };
        var back = PatchSerializer.Deserialize(PatchSerializer.Serialize(patch));
        Assert.Equal("Said sarcastically.", back.NodeComments[42]);
    }

    [Fact]
    public void Serialize_V2_AddedNodeHasNoTextInJson()
    {
        var node = new NodeEditSnapshot(
            99, false, SpeakerCategory.Npc, "", "", "Hello", "", // DefaultText = "Hello"
            "Conversation", "None", "", "", "", false, false, [], [], []);
        var patch = new ConversationPatch("conv", 2, [node], [], []);
        var json  = PatchSerializer.Serialize(patch);
        // DefaultText must NOT appear in the serialised AddedNodes
        Assert.DoesNotContain("\"DefaultText\"", json.Split("AddedNodes")[1].Split("ModifiedNodes")[0]);
    }

    [Fact]
    public void IsEmpty_TrueWhenOnlyDefaultProperties()
    {
        var patch = new ConversationPatch("conv", 2, [], [], []);
        Assert.True(patch.IsEmpty);
    }

    [Fact]
    public void IsEmpty_FalseWhenNodeCommentsPresent()
    {
        var patch = new ConversationPatch("conv", 2, [], [], [])
        {
            NodeComments = new Dictionary<int, string> { [1] = "note" }
        };
        Assert.False(patch.IsEmpty);
    }

    [Fact]
    public void IsEmpty_FalseWhenTranslationsPresent()
    {
        var patch = new ConversationPatch("conv", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { ["en"] = [new NodeTranslation(1, "Hi", "")] }
        };
        Assert.False(patch.IsEmpty);
    }
}
