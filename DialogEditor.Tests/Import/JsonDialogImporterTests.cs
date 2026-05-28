using DialogEditor.Core.Import;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Import;

public class JsonDialogImporterTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string WriteTempJson(string content, string? filename = null)
    {
        var path = filename is not null
            ? Path.Combine(Path.GetTempPath(), filename)
            : Path.ChangeExtension(Path.GetTempFileName(), ".json");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort cleanup */ }
    }

    private static readonly JsonDialogImporter Importer = new();

    // ── Node count ────────────────────────────────────────────────────────

    [Fact]
    public void Import_BasicConversation_ReturnsCorrectNodeCount()
    {
        var path = WriteTempJson("""
            {
              "name": "my_conv",
              "nodes": [
                { "id": 1, "speakerCategory": "Npc", "defaultText": "Hello!", "links": [2] },
                { "id": 2, "speakerCategory": "Player", "defaultText": "Yes." },
                { "id": 3, "speakerCategory": "Npc", "defaultText": "Goodbye." }
              ]
            }
            """);

        var result = Importer.Import(path);

        Assert.Equal(3, result.Nodes.Count);
    }

    // ── SuggestedName ─────────────────────────────────────────────────────

    [Fact]
    public void Import_NameFromJsonField_UsesThatName()
    {
        var path = WriteTempJson("""
            {
              "name": "my_conv",
              "nodes": [
                { "id": 1, "speakerCategory": "Npc", "defaultText": "Hello!" }
              ]
            }
            """);

        var result = Importer.Import(path);

        Assert.Equal("my_conv", result.SuggestedName);
    }

    [Fact]
    public void Import_NameAbsent_UsesFilename()
    {
        var path = WriteTempJson("""
            {
              "nodes": [
                { "id": 1, "speakerCategory": "Npc", "defaultText": "Hello!" }
              ]
            }
            """, filename: "village_inn.json");

        var result = Importer.Import(path);

        Assert.Equal("village_inn", result.SuggestedName);
    }

    // ── Links ─────────────────────────────────────────────────────────────

    [Fact]
    public void Import_Links_ProducesCorrectLinkSnapshots()
    {
        var path = WriteTempJson("""
            {
              "nodes": [
                { "id": 5, "speakerCategory": "Npc", "defaultText": "Text", "links": [6, 7] },
                { "id": 6, "speakerCategory": "Npc", "defaultText": "A" },
                { "id": 7, "speakerCategory": "Npc", "defaultText": "B" }
              ]
            }
            """);

        var result = Importer.Import(path);

        var node5 = result.Nodes.Single(n => n.NodeId == 5);
        Assert.Equal(2, node5.Links.Count);
        Assert.Contains(node5.Links, l => l.ToNodeId == 6);
        Assert.Contains(node5.Links, l => l.ToNodeId == 7);

        var link = node5.Links.Single(l => l.ToNodeId == 6);
        Assert.Equal(5, link.FromNodeId);
        Assert.Equal(1f, link.RandomWeight);
        Assert.Equal("", link.QuestionNodeTextDisplay);
        Assert.Null(link.Conditions);
    }

    // ── Optional fields / defaults ────────────────────────────────────────

    [Fact]
    public void Import_MissingOptionalFields_UsesDefaults()
    {
        var path = WriteTempJson("""
            {
              "nodes": [
                { "id": 1 }
              ]
            }
            """);

        var result = Importer.Import(path);

        var node = result.Nodes.Single();
        Assert.Equal("", node.FemaleText);
        Assert.Equal("Conversation", node.DisplayType);
        Assert.Equal("None", node.Persistence);
        Assert.Empty(node.Links);
        Assert.Equal(SpeakerCategory.Npc, node.SpeakerCategory);
        Assert.Equal("", node.DefaultText);
    }

    // ── Error handling ────────────────────────────────────────────────────

    [Fact]
    public void Import_NoNodesArray_ThrowsFormatException()
    {
        var path = WriteTempJson("""
            {
              "name": "my_conv",
              "title": "something else"
            }
            """);

        Assert.Throws<FormatException>(() => Importer.Import(path));
    }

    // ── Texts match nodes ─────────────────────────────────────────────────

    [Fact]
    public void Import_TextsMatchNodes()
    {
        var path = WriteTempJson("""
            {
              "nodes": [
                { "id": 1, "defaultText": "Hello!", "femaleText": "Hi there!" },
                { "id": 2, "defaultText": "Yes." }
              ]
            }
            """);

        var result = Importer.Import(path);

        Assert.Equal(result.Nodes.Count, result.Texts.Count);
        foreach (var node in result.Nodes)
        {
            var text = result.Texts.SingleOrDefault(t => t.NodeId == node.NodeId);
            Assert.NotNull(text);
            Assert.Equal(node.DefaultText, text.DefaultText);
            Assert.Equal(node.FemaleText, text.FemaleText);
        }
    }

    // ── Warnings ──────────────────────────────────────────────────────────

    [Fact]
    public void Import_Json_HasNoWarnings()
    {
        var path = WriteTempJson("""
            { "name": "c", "nodes": [ { "id": 1, "speakerCategory": "Npc", "defaultText": "Hi" } ] }
            """);

        var result = Importer.Import(path);

        Assert.Empty(result.Warnings);
    }

    // ── IsPlayerChoice ────────────────────────────────────────────────────

    [Fact]
    public void Import_PlayerSpeakerCategory_SetsIsPlayerChoiceTrue()
    {
        var path = WriteTempJson("""
            {
              "nodes": [
                { "id": 1, "speakerCategory": "Player", "defaultText": "Pick one." },
                { "id": 2, "speakerCategory": "Npc", "defaultText": "NPC line." }
              ]
            }
            """);

        var result = Importer.Import(path);

        Assert.True(result.Nodes.Single(n => n.NodeId == 1).IsPlayerChoice);
        Assert.False(result.Nodes.Single(n => n.NodeId == 2).IsPlayerChoice);
    }
}
