using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class LocalizationExportService
{
    public static void Export(
        DialogProject project,
        string outputPath,
        LocalizationExportFormat format,
        string sourceLanguage)
    {
        var rows = BuildRows(project, sourceLanguage);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        switch (format)
        {
            case LocalizationExportFormat.Csv:   WriteCsv(outputPath, rows);                     break;
            case LocalizationExportFormat.Json:  WriteJson(outputPath, sourceLanguage, rows);    break;
            case LocalizationExportFormat.Xliff: WriteXliff(outputPath, sourceLanguage, rows);   break;
        }
    }

    private record ExportRow(
        string ConversationName,
        int    NodeId,
        string WriterComment,
        string SourceDefaultText,
        string SourceFemaleText);

    private static List<ExportRow> BuildRows(DialogProject project, string sourceLanguage)
    {
        var rows = new List<ExportRow>();
        foreach (var (_, patch) in project.Patches)
        {
            if (!patch.Translations.TryGetValue(sourceLanguage, out var translations)) continue;
            foreach (var t in translations)
            {
                var comment = patch.NodeComments.TryGetValue(t.NodeId, out var c) ? c : string.Empty;
                rows.Add(new ExportRow(patch.ConversationName, t.NodeId, comment, t.DefaultText, t.FemaleText));
            }
        }
        return rows;
    }

    // ── CSV ──────────────────────────────────────────────────────────────

    private static void WriteCsv(string path, List<ExportRow> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ConversationName,NodeId,WriterComment,SourceDefaultText,SourceFemaleText,TranslatedDefaultText,TranslatedFemaleText");
        foreach (var r in rows)
        {
            sb.Append(CsvField(r.ConversationName)); sb.Append(',');
            sb.Append(r.NodeId);                     sb.Append(',');
            sb.Append(CsvField(r.WriterComment));    sb.Append(',');
            sb.Append(CsvField(r.SourceDefaultText));sb.Append(',');
            sb.Append(CsvField(r.SourceFemaleText)); sb.Append(',');
            sb.Append("\"\""); sb.Append(','); // TranslatedDefaultText — empty placeholder
            sb.AppendLine();  // TranslatedFemaleText — empty
        }
        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string CsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }

    // ── JSON ─────────────────────────────────────────────────────────────

    private static void WriteJson(string path, string sourceLanguage, List<ExportRow> rows)
    {
        var obj = new
        {
            sourceLanguage,
            targetLanguage = string.Empty,
            entries = rows.Select(r => new
            {
                conversation        = r.ConversationName,
                nodeId              = r.NodeId,
                writerComment       = r.WriterComment,
                sourceDefaultText   = r.SourceDefaultText,
                sourceFemaleText    = r.SourceFemaleText,
                translatedDefaultText = string.Empty,
                translatedFemaleText  = string.Empty,
            }),
        };
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json, Encoding.UTF8);
    }

    // ── XLIFF 1.2 ─────────────────────────────────────────────────────────

    private static void WriteXliff(string path, string sourceLanguage, List<ExportRow> rows)
    {
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
        var byConv = rows.GroupBy(r => r.ConversationName);

        var files = byConv.Select(g =>
        {
            var units = g.Select(r =>
            {
                var unit = new XElement(ns + "trans-unit",
                    new XAttribute("id", $"node_{r.NodeId}"),
                    new XElement(ns + "source", r.SourceDefaultText),
                    new XElement(ns + "target"));

                if (!string.IsNullOrEmpty(r.SourceFemaleText))
                    unit.Add(new XElement(ns + "note",
                        new XAttribute("from", "female"), r.SourceFemaleText));

                if (!string.IsNullOrEmpty(r.WriterComment))
                    unit.Add(new XElement(ns + "note",
                        new XAttribute("from", "writer"), r.WriterComment));

                return unit;
            });

            return new XElement(ns + "file",
                new XAttribute("source-language", sourceLanguage),
                new XAttribute("target-language", string.Empty),
                new XAttribute("datatype", "plaintext"),
                new XAttribute("original", g.Key),
                new XElement(ns + "body", units));
        });

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement(ns + "xliff",
                new XAttribute("version", "1.2"),
                files));

        doc.Save(path);
    }
}
