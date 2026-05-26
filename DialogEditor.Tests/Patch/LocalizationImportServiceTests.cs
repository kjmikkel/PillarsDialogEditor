using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class LocalizationImportServiceTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public LocalizationImportServiceTests() => Directory.CreateDirectory(_tempDir);
    public void Dispose() => Directory.Delete(_tempDir, true);

    private static DialogProject BaseProject()
    {
        var patch = new ConversationPatch("conv", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "Hello", ""), new NodeTranslation(2, "Bye", "")],
            },
        };
        return DialogProject.Empty("Test").WithPatch(patch);
    }

    private string ExportAndGetPath(LocalizationExportFormat fmt)
    {
        var ext  = fmt == LocalizationExportFormat.Csv  ? ".csv"
                 : fmt == LocalizationExportFormat.Json ? ".json" : ".xlf";
        var path = Path.Combine(_tempDir, "export" + ext);
        LocalizationExportService.Export(BaseProject(), path, fmt, "en");
        return path;
    }

    // ── CSV round-trip ────────────────────────────────────────────────────

    [Fact]
    public void Import_Csv_RoundTrip_StoresTranslations()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Csv);

        // Manually edit the CSV to fill in translations
        var lines = File.ReadAllLines(exportPath).ToList();
        // Replace empty translated columns in rows 1 and 2
        lines[1] = lines[1].TrimEnd(',') + "Bonjour,";
        lines[2] = lines[2].TrimEnd(',') + "Au revoir,";
        File.WriteAllLines(exportPath, lines);

        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Csv, "fr");
        var frTrans = result.Patches["conv"].Translations["fr"];
        Assert.Equal(2, frTrans.Count);
        Assert.Equal("Bonjour",   frTrans.First(t => t.NodeId == 1).DefaultText);
        Assert.Equal("Au revoir", frTrans.First(t => t.NodeId == 2).DefaultText);
    }

    [Fact]
    public void Import_Csv_EmptyTranslatedText_Excluded()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Csv);
        // Leave all translated columns empty (default export state)
        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Csv, "fr");
        Assert.False(result.Patches["conv"].Translations.ContainsKey("fr"));
    }

    [Fact]
    public void Import_Csv_UnknownConversation_SilentlyIgnored()
    {
        // Build a CSV with a conversation name not in the project
        var csv = "ConversationName,NodeId,WriterComment,SourceDefaultText,SourceFemaleText,TranslatedDefaultText,TranslatedFemaleText\n"
                + "unknown_conv,1,,Hello,,Bonjour,\n";
        var path = Path.Combine(_tempDir, "unknown.csv");
        File.WriteAllText(path, csv);
        var result = LocalizationImportService.Import(BaseProject(), path,
                                                      LocalizationExportFormat.Csv, "fr");
        Assert.False(result.Patches.ContainsKey("unknown_conv"));
    }

    // ── JSON round-trip ───────────────────────────────────────────────────

    [Fact]
    public void Import_Json_RoundTrip_StoresTranslations()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Json);
        var json = File.ReadAllText(exportPath)
            .Replace("\"translatedDefaultText\": \"\"", "\"translatedDefaultText\": \"Bonjour\"");
        File.WriteAllText(exportPath, json);

        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Json, "fr");
        var frTrans = result.Patches["conv"].Translations["fr"];
        Assert.Contains(frTrans, t => t.DefaultText == "Bonjour");
    }

    [Fact]
    public void Import_Json_WriterComments_NotImported()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Json);
        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Json, "fr");
        // NodeComments should be unchanged (import never writes them)
        Assert.Empty(result.Patches["conv"].NodeComments);
    }

    // ── XLIFF round-trip ──────────────────────────────────────────────────

    [Fact]
    public void Import_Xliff_RoundTrip_StoresTranslations()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Xliff);
        var content = File.ReadAllText(exportPath)
            .Replace("<target />", "<target>Bonjour</target>");
        File.WriteAllText(exportPath, content);

        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Xliff, "fr");
        var frTrans = result.Patches["conv"].Translations["fr"];
        Assert.NotEmpty(frTrans);
    }

    // ── Author's own language ─────────────────────────────────────────────

    [Fact]
    public void Import_AuthorLanguage_WorksLikeAnyOther()
    {
        var exportPath = ExportAndGetPath(LocalizationExportFormat.Csv);
        var lines = File.ReadAllLines(exportPath).ToList();
        lines[1] = lines[1].TrimEnd(',') + "Updated Hello,";
        File.WriteAllLines(exportPath, lines);

        var result = LocalizationImportService.Import(BaseProject(), exportPath,
                                                      LocalizationExportFormat.Csv, "en");
        var enTrans = result.Patches["conv"].Translations["en"];
        Assert.Contains(enTrans, t => t.NodeId == 1 && t.DefaultText == "Updated Hello");
    }
}
