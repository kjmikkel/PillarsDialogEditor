using System.Text.Json;
using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Patch;

public static class LocalizationImportService
{
    public static DialogProject Import(
        DialogProject project,
        string inputPath,
        LocalizationExportFormat format,
        string language)
    {
        var grouped = format switch
        {
            LocalizationExportFormat.Csv   => ParseCsv(inputPath),
            LocalizationExportFormat.Json  => ParseJson(inputPath),
            LocalizationExportFormat.Xliff => ParseXliff(inputPath),
            _ => throw new ArgumentOutOfRangeException(nameof(format))
        };

        var result = project;
        foreach (var (convName, translations) in grouped)
        {
            if (!result.Patches.TryGetValue(convName, out var patch)) continue;
            var nonEmpty = translations
                .Where(t => !string.IsNullOrEmpty(t.DefaultText) || !string.IsNullOrEmpty(t.FemaleText))
                .ToList();
            if (nonEmpty.Count == 0) continue;

            var newTranslations = new Dictionary<string, IReadOnlyList<NodeTranslation>>(patch.Translations)
            {
                [language] = nonEmpty
            };
            var updatedPatch = patch with { Translations = newTranslations };
            result = result.WithPatch(updatedPatch);
        }
        return result;
    }

    public static string? DetectLanguage(string path, LocalizationExportFormat format)
    {
        try
        {
            return format switch
            {
                LocalizationExportFormat.Csv   => null,
                LocalizationExportFormat.Json  => DetectLanguageJson(path),
                LocalizationExportFormat.Xliff => DetectLanguageXliff(path),
                _                              => null,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? DetectLanguageJson(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("targetLanguage", out var prop)) return null;
        var value = prop.GetString();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static string? DetectLanguageXliff(string path)
    {
        var doc = XDocument.Load(path);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";
        var file = doc.Descendants(ns + "file").FirstOrDefault();
        var value = file?.Attribute("target-language")?.Value;
        return string.IsNullOrEmpty(value) ? null : value;
    }

    // ── CSV ──────────────────────────────────────────────────────────────

    private static Dictionary<string, List<NodeTranslation>> ParseCsv(string path)
    {
        var result = new Dictionary<string, List<NodeTranslation>>();
        var lines  = File.ReadAllLines(path);
        for (int i = 1; i < lines.Length; i++) // skip header
        {
            var fields = SplitCsvLine(lines[i]);
            if (fields.Count < 7) continue;
            var convName = fields[0];
            if (!int.TryParse(fields[1], out var nodeId)) continue;
            // fields[2] = WriterComment      (ignored on import)
            // fields[3] = SourceDefaultText  (ignored)
            // fields[4] = SourceFemaleText   (ignored)
            // fields[5] = TranslatedDefaultText
            // fields[6] = TranslatedFemaleText
            var transDefault = fields[5].Trim('\r');
            var transFemale  = fields[6].Trim('\r');

            if (!result.ContainsKey(convName)) result[convName] = [];
            result[convName].Add(new NodeTranslation(nodeId, transDefault, transFemale));
        }
        return result;
    }

    private static List<string> SplitCsvLine(string line)
    {
        var fields   = new List<string>();
        var current  = new System.Text.StringBuilder();
        bool inQuote = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuote)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                { current.Append('"'); i++; }
                else if (c == '"') inQuote = false;
                else current.Append(c);
            }
            else
            {
                if (c == '"') inQuote = true;
                else if (c == ',') { fields.Add(current.ToString()); current.Clear(); }
                else current.Append(c);
            }
        }
        fields.Add(current.ToString());
        return fields;
    }

    // ── JSON ─────────────────────────────────────────────────────────────

    private static Dictionary<string, List<NodeTranslation>> ParseJson(string path)
    {
        var result = new Dictionary<string, List<NodeTranslation>>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (!doc.RootElement.TryGetProperty("entries", out var entries)) return result;
        foreach (var entry in entries.EnumerateArray())
        {
            var convName     = entry.GetProperty("conversation").GetString() ?? string.Empty;
            var nodeId       = entry.GetProperty("nodeId").GetInt32();
            var transDefault = entry.GetProperty("translatedDefaultText").GetString() ?? string.Empty;
            var transFemale  = entry.GetProperty("translatedFemaleText").GetString()  ?? string.Empty;
            if (!result.ContainsKey(convName)) result[convName] = [];
            result[convName].Add(new NodeTranslation(nodeId, transDefault, transFemale));
        }
        return result;
    }

    // ── XLIFF 1.2 ────────────────────────────────────────────────────────

    private static Dictionary<string, List<NodeTranslation>> ParseXliff(string path)
    {
        var result = new Dictionary<string, List<NodeTranslation>>();
        var doc    = XDocument.Load(path);
        XNamespace ns = "urn:oasis:names:tc:xliff:document:1.2";

        foreach (var file in doc.Descendants(ns + "file"))
        {
            var convName = file.Attribute("original")?.Value ?? string.Empty;
            result[convName] = [];
            foreach (var unit in file.Descendants(ns + "trans-unit"))
            {
                var idAttr = unit.Attribute("id")?.Value ?? string.Empty;
                if (!idAttr.StartsWith("node_")) continue;
                if (!int.TryParse(idAttr[5..], out var nodeId)) continue;
                var target = unit.Element(ns + "target")?.Value ?? string.Empty;
                var female = unit.Elements(ns + "note")
                                 .FirstOrDefault(n => n.Attribute("from")?.Value == "female")
                                 ?.Value ?? string.Empty;
                result[convName].Add(new NodeTranslation(nodeId, target, female));
            }
        }
        return result;
    }
}
