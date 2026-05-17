using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class DialogProjectTests
{
    private static ConversationPatch MakePatch(string name) =>
        new(name, ConversationPatch.CurrentSchemaVersion,
            [new NodeEditSnapshot(1, false, SpeakerCategory.Npc, "", "", "text", "",
                "Conversation", "None", "", "", "", false, false, [], [])],
            [2],
            []);

    // ── DialogProject.WithPatch ───────────────────────────────────────────

    [Fact]
    public void WithPatch_AddsPatchByName()
    {
        var project = DialogProject.Empty("mod")
            .WithPatch(MakePatch("eder"));
        Assert.Single(project.Patches);
        Assert.True(project.Patches.ContainsKey("eder"));
    }

    [Fact]
    public void WithPatch_UpsertReplacesExisting()
    {
        var project = DialogProject.Empty("mod")
            .WithPatch(MakePatch("eder"))
            .WithPatch(MakePatch("eder"));
        Assert.Single(project.Patches);
    }

    [Fact]
    public void WithPatch_MultipleConversations_AllPresent()
    {
        var project = DialogProject.Empty("mod")
            .WithPatch(MakePatch("eder"))
            .WithPatch(MakePatch("aloth"));
        Assert.Equal(2, project.Patches.Count);
    }

    // ── Round-trip serialization ──────────────────────────────────────────

    [Fact]
    public void Serialize_RoundTrip_PreservesAllFields()
    {
        var original = DialogProject.Empty("My Mod")
            .WithPatch(MakePatch("eder"))
            .WithPatch(MakePatch("aloth"));

        var json         = DialogProjectSerializer.Serialize(original);
        var deserialized = DialogProjectSerializer.Deserialize(json);

        Assert.Equal("My Mod",                        deserialized.Name);
        Assert.Equal(DialogProject.CurrentSchemaVersion, deserialized.SchemaVersion);
        Assert.Equal(2,                               deserialized.Patches.Count);
        Assert.True(deserialized.Patches.ContainsKey("eder"));
        Assert.True(deserialized.Patches.ContainsKey("aloth"));
        Assert.Single(deserialized.Patches["eder"].AddedNodes);
        Assert.Single(deserialized.Patches["eder"].DeletedNodeIds);
    }

    [Fact]
    public void Serialize_ProducesHumanReadableJson()
    {
        var json = DialogProjectSerializer.Serialize(DialogProject.Empty("test"));
        Assert.Contains("\"Name\"", json);
        Assert.Contains('\n', json);
    }
}
