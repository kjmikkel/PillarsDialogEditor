using System.IO;
using System.Text.Json;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class NewConversationTests
{
    // ── DialogProject round-trip ──────────────────────────────────────────

    [Fact]
    public void DialogProject_WithNewConversation_StoresName()
    {
        var project = DialogProject.Empty("test").WithNewConversation("my_conv");
        Assert.Contains("my_conv", project.NewConversations ?? []);
    }

    [Fact]
    public void DialogProject_NewConversations_SerializesAndDeserializes()
    {
        var project = DialogProject.Empty("test")
            .WithNewConversation("conv_a")
            .WithNewConversation("conv_b");

        var json     = JsonSerializer.Serialize(project);
        var restored = JsonSerializer.Deserialize<DialogProject>(json)!;

        Assert.Equal(2, (restored.NewConversations ?? []).Count);
        Assert.Contains("conv_a", restored.NewConversations!);
        Assert.Contains("conv_b", restored.NewConversations!);
    }

    [Fact]
    public void DialogProject_WithoutNewConversations_DeserializesCleanly()
    {
        // Old .dialogproject files without the field must still load
        var json     = """{"Name":"x","SchemaVersion":1,"Patches":{}}""";
        var restored = JsonSerializer.Deserialize<DialogProject>(json)!;
        Assert.Null(restored.NewConversations);
    }

    // ── Poe1GameDataProvider ──────────────────────────────────────────────

    [Fact]
    public void Poe1_BuildNewConversationFile_ReturnsCorrectExtension()
    {
        var provider = new Poe1GameDataProvider(@"C:\FakeGameRoot");
        var file     = provider.BuildNewConversationFile("test_conv");
        Assert.EndsWith(".conversation", file.ConversationPath);
        Assert.Contains("test_conv", file.ConversationPath);
    }

    [Fact]
    public void Poe1_BuildNewConversationFile_Name_MatchesInput()
    {
        var provider = new Poe1GameDataProvider(@"C:\FakeGameRoot");
        var file     = provider.BuildNewConversationFile("my_dialogue");
        Assert.Equal("my_dialogue", file.Name);
    }

    [Fact]
    public void Poe1_InitializeConversationFile_WritesParseableXml()
    {
        var dir  = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            // Simulate a game root with the PoE1 folder structure
            var convDir = Path.Combine(dir, "PillarsOfEternity_Data", "data", "conversations");
            Directory.CreateDirectory(convDir);

            var provider = new Poe1GameDataProvider(dir);
            var file     = provider.BuildNewConversationFile("test_blank");

            provider.InitializeConversationFile(file);

            Assert.True(File.Exists(file.ConversationPath));
            // Must be parseable — parser must not throw
            var nodes = Poe1ConversationParser.ParseFile(file.ConversationPath);
            Assert.Empty(nodes);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ── Poe2GameDataProvider ──────────────────────────────────────────────

    [Fact]
    public void Poe2_BuildNewConversationFile_ReturnsCorrectExtension()
    {
        var provider = new Poe2GameDataProvider(@"C:\FakeGameRoot");
        var file     = provider.BuildNewConversationFile("test_conv");
        Assert.EndsWith(".conversationbundle", file.ConversationPath);
    }

    [Fact]
    public void Poe2_InitializeConversationFile_WritesParseableJson()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var convDir = Path.Combine(dir,
                "PillarsOfEternityII_Data", "exported", "design", "conversations");
            Directory.CreateDirectory(convDir);

            var provider = new Poe2GameDataProvider(dir);
            var file     = provider.BuildNewConversationFile("test_blank");

            provider.InitializeConversationFile(file);

            Assert.True(File.Exists(file.ConversationPath));
            var nodes = Poe2ConversationParser.ParseFile(file.ConversationPath);
            Assert.Empty(nodes);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
