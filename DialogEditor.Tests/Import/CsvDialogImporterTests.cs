using DialogEditor.Core.Import;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Import;

public class CsvDialogImporterTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    private string WriteTempCsv(string content)
    {
        var path = Path.ChangeExtension(Path.GetTempFileName(), ".csv");
        File.WriteAllText(path, content);
        _tempFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort cleanup */ }
    }

    private static readonly CsvDialogImporter Importer = new();

    // ── Node count ────────────────────────────────────────────────────────

    [Fact]
    public void Import_BasicThreeNodeConversation_ReturnsCorrectNodeCount()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText,FemaleText,LinksTo,DisplayType,Persistence
            1,Npc,Hello,,2,Conversation,None
            2,Player,Yes,,3,Conversation,None
            3,Npc,Goodbye,,,Conversation,None
            """);

        var result = Importer.Import(path);

        Assert.Equal(3, result.Nodes.Count);
    }

    // ── Links ─────────────────────────────────────────────────────────────

    [Fact]
    public void Import_LinksTo_ParsesSemicolonSeparatedIds()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText,FemaleText,LinksTo,DisplayType,Persistence
            1,Npc,Hello,,2;3,Conversation,None
            2,Npc,Option A,,,Conversation,None
            3,Npc,Option B,,,Conversation,None
            """);

        var result = Importer.Import(path);

        var node1 = result.Nodes.Single(n => n.NodeId == 1);
        Assert.Equal(2, node1.Links.Count);
        Assert.Contains(node1.Links, l => l.ToNodeId == 2);
        Assert.Contains(node1.Links, l => l.ToNodeId == 3);
    }

    // ── Optional columns ──────────────────────────────────────────────────

    [Fact]
    public void Import_MissingOptionalColumns_UsesDefaults()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText
            1,Npc,Hello
            """);

        var result = Importer.Import(path);

        var node = result.Nodes.Single();
        Assert.Equal("", node.FemaleText);
        Assert.Equal("Conversation", node.DisplayType);
        Assert.Equal("None", node.Persistence);
    }

    // ── IsPlayerChoice ────────────────────────────────────────────────────

    [Fact]
    public void Import_PlayerSpeakerCategory_SetsIsPlayerChoiceTrue()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText,FemaleText,LinksTo,DisplayType,Persistence
            1,Player,Pick one,,,Conversation,None
            2,Npc,NPC line,,,Conversation,None
            """);

        var result = Importer.Import(path);

        Assert.True(result.Nodes.Single(n => n.NodeId == 1).IsPlayerChoice);
        Assert.False(result.Nodes.Single(n => n.NodeId == 2).IsPlayerChoice);
    }

    // ── SuggestedName ─────────────────────────────────────────────────────

    [Fact]
    public void Import_SuggestedName_ComesFromFilename()
    {
        var dir = Path.GetTempPath();
        var path = Path.Combine(dir, "village_inn.csv");
        File.WriteAllText(path, """
            NodeId,SpeakerCategory,DefaultText
            1,Npc,Hello
            """);
        _tempFiles.Add(path);

        var result = Importer.Import(path);

        Assert.Equal("village_inn", result.SuggestedName);
    }

    // ── Texts match nodes ─────────────────────────────────────────────────

    [Fact]
    public void Import_TextsMatchNodes()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText,FemaleText,LinksTo,DisplayType,Persistence
            1,Npc,Hello,Hi there,2,Conversation,None
            2,Player,Yes,,,Conversation,None
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

    // ── Error handling ────────────────────────────────────────────────────

    [Fact]
    public void Import_MissingHeaderRow_ThrowsFormatException()
    {
        var path = WriteTempCsv("");

        Assert.Throws<FormatException>(() => Importer.Import(path));
    }

    [Fact]
    public void Import_HeaderMissingNodeIdColumn_ThrowsFormatException()
    {
        var path = WriteTempCsv("""
            SpeakerCategory,DefaultText
            Npc,Hello
            """);

        Assert.Throws<FormatException>(() => Importer.Import(path));
    }

    // ── Link metadata ─────────────────────────────────────────────────────

    [Fact]
    public void Import_Links_HaveCorrectFromNodeIdAndDefaultWeight()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText,FemaleText,LinksTo,DisplayType,Persistence
            5,Npc,Text,,6,Conversation,None
            6,Npc,Text2,,,Conversation,None
            """);

        var result = Importer.Import(path);

        var link = result.Nodes.Single(n => n.NodeId == 5).Links.Single();
        Assert.Equal(5, link.FromNodeId);
        Assert.Equal(6, link.ToNodeId);
        Assert.Equal(1f, link.RandomWeight);
        Assert.Equal("", link.QuestionNodeTextDisplay);
        Assert.Null(link.Conditions);
    }

    // ── Quoted fields ─────────────────────────────────────────────────────

    [Fact]
    public void Import_QuotedFields_ParsedCorrectly()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText,FemaleText,LinksTo,DisplayType,Persistence
            1,Npc,"Hello, traveler!",,2,Conversation,None
            2,Npc,Farewell,,,Conversation,None
            """);

        var result = Importer.Import(path);

        var node = result.Nodes.Single(n => n.NodeId == 1);
        Assert.Equal("Hello, traveler!", node.DefaultText);
    }

    // ── Non-numeric NodeId ────────────────────────────────────────────────

    [Fact]
    public void Import_NonNumericNodeId_ThrowsFormatExceptionWithRowContext()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText
            abc,Npc,Hello
            """);

        var ex = Assert.Throws<FormatException>(() => Importer.Import(path));
        Assert.Contains("Row 2", ex.Message);
        Assert.Contains("abc", ex.Message);
    }

    // ── Unclosed quoted field ─────────────────────────────────────────────

    [Fact]
    public void Import_UnclosedQuotedField_ThrowsFormatExceptionWithRowContext()
    {
        var path = WriteTempCsv("NodeId,SpeakerCategory,DefaultText\n1,Npc,\"unclosed");

        var ex = Assert.Throws<FormatException>(() => Importer.Import(path));
        Assert.Contains("Row 2", ex.Message);
        Assert.Contains("unclosed", ex.Message);
    }

    // ── Warnings ──────────────────────────────────────────────────────────

    [Fact]
    public void Import_Csv_HasNoWarnings()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText
            1,Npc,Hello
            """);

        var result = Importer.Import(path);

        Assert.Empty(result.Warnings);
    }

    // ── SpeakerCategory case-insensitivity and fallback ──────────────────

    [Fact]
    public void Import_InvalidSpeakerCategory_DefaultsToNpc()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText
            1,UNKNOWN_CATEGORY,Hello
            """);

        var result = Importer.Import(path);

        Assert.Equal(SpeakerCategory.Npc, result.Nodes.Single().SpeakerCategory);
    }

    [Fact]
    public void Import_SpeakerCategory_IsCaseInsensitive()
    {
        var path = WriteTempCsv("""
            NodeId,SpeakerCategory,DefaultText
            1,narrator,Hello
            """);

        var result = Importer.Import(path);

        Assert.Equal(SpeakerCategory.Narrator, result.Nodes.Single().SpeakerCategory);
    }
}
