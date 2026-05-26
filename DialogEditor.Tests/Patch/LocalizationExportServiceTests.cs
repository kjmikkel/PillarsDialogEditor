using System.Text.Json;
using System.Xml.Linq;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class LocalizationExportServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public LocalizationExportServiceTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, true);

    private static DialogProject MakeProject()
    {
        var patch = new ConversationPatch("test_conv", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [
                    new NodeTranslation(1, "Hello", ""),
                    new NodeTranslation(2, "Goodbye", "Farewell"),
                ],
            },
            NodeComments = new Dictionary<int, string>
            {
                [1] = "Greeting on entry",
            },
        };
        return DialogProject.Empty("Test").WithPatch(patch);
    }

    // ── CSV ──────────────────────────────────────────────────────────────

    [Fact]
    public void Export_Csv_HeaderRow()
    {
        var path = Path.Combine(_tempDir, "out.csv");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Csv, "en");
        var lines = File.ReadAllLines(path);
        Assert.Equal(
            "ConversationName,NodeId,WriterComment,SourceDefaultText,SourceFemaleText,TranslatedDefaultText,TranslatedFemaleText",
            lines[0]);
    }

    [Fact]
    public void Export_Csv_NodeRowsContainSourceText()
    {
        var path = Path.Combine(_tempDir, "out.csv");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Csv, "en");
        var content = File.ReadAllText(path);
        Assert.Contains("Hello", content);
        Assert.Contains("Goodbye", content);
        Assert.Contains("Farewell", content);
    }

    [Fact]
    public void Export_Csv_WriterCommentIncluded()
    {
        var path = Path.Combine(_tempDir, "out.csv");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Csv, "en");
        var content = File.ReadAllText(path);
        Assert.Contains("Greeting on entry", content);
    }

    [Fact]
    public void Export_Csv_MissingSourceLanguage_ProducesHeaderOnly()
    {
        var path = Path.Combine(_tempDir, "out.csv");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Csv, "fr");
        var lines = File.ReadAllLines(path);
        Assert.Single(lines); // header only
    }

    [Fact]
    public void Export_Csv_StructuralPatchWithNoTranslations_ProducesHeaderOnly()
    {
        var patch   = new ConversationPatch("c", 2, [], [], []);
        var project = DialogProject.Empty("T").WithPatch(patch);
        var path    = Path.Combine(_tempDir, "out.csv");
        LocalizationExportService.Export(project, path, LocalizationExportFormat.Csv, "en");
        var lines = File.ReadAllLines(path);
        Assert.Single(lines);
    }

    // ── JSON ─────────────────────────────────────────────────────────────

    [Fact]
    public void Export_Json_SourceLanguageRecorded()
    {
        var path = Path.Combine(_tempDir, "out.json");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Json, "en");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        Assert.Equal("en", doc.RootElement.GetProperty("sourceLanguage").GetString());
    }

    [Fact]
    public void Export_Json_EntriesHaveWriterComment()
    {
        var path = Path.Combine(_tempDir, "out.json");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Json, "en");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();
        var node1   = entries.First(e => e.GetProperty("nodeId").GetInt32() == 1);
        Assert.Equal("Greeting on entry", node1.GetProperty("writerComment").GetString());
    }

    [Fact]
    public void Export_Json_EmptyCommentWhenNoneSet()
    {
        var path = Path.Combine(_tempDir, "out.json");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Json, "en");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var entries = doc.RootElement.GetProperty("entries").EnumerateArray().ToList();
        var node2   = entries.First(e => e.GetProperty("nodeId").GetInt32() == 2);
        Assert.Equal("", node2.GetProperty("writerComment").GetString());
    }

    // ── XLIFF ────────────────────────────────────────────────────────────

    [Fact]
    public void Export_Xliff_TransUnitsCreated()
    {
        var path = Path.Combine(_tempDir, "out.xlf");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Xliff, "en");
        var doc   = XDocument.Load(path);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
        var units = doc.Descendants(ns + "trans-unit").ToList();
        Assert.Equal(2, units.Count);
    }

    [Fact]
    public void Export_Xliff_WriterCommentAsNote()
    {
        var path = Path.Combine(_tempDir, "out.xlf");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Xliff, "en");
        var doc  = XDocument.Load(path);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
        var unit = doc.Descendants(ns + "trans-unit")
                      .First(u => u.Attribute("id")!.Value == "node_1");
        var writerNote = unit.Elements(ns + "note")
                             .FirstOrDefault(n => n.Attribute("from")?.Value == "writer");
        Assert.NotNull(writerNote);
        Assert.Equal("Greeting on entry", writerNote.Value);
    }

    [Fact]
    public void Export_Xliff_FemaleTextAsNote()
    {
        var path = Path.Combine(_tempDir, "out.xlf");
        LocalizationExportService.Export(MakeProject(), path, LocalizationExportFormat.Xliff, "en");
        var doc  = XDocument.Load(path);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
        var unit = doc.Descendants(ns + "trans-unit")
                      .First(u => u.Attribute("id")!.Value == "node_2");
        var femaleNote = unit.Elements(ns + "note")
                             .FirstOrDefault(n => n.Attribute("from")?.Value == "female");
        Assert.NotNull(femaleNote);
        Assert.Equal("Farewell", femaleNote.Value);
    }
}
