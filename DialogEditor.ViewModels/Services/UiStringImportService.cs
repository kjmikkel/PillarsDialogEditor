using System.Text;
using System.Xml.Linq;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Reads a translated UI-strings CSV (produced by <see cref="UiStringExportService"/>)
/// and writes one AXAML overlay file per source file into <paramref name="outputDirectory"/>.
/// Overlay filenames follow the pattern "Strings.de.axaml" (base name + language + .axaml).
/// Only rows with a non-empty Translation are emitted.
/// </summary>
public static class UiStringImportService
{
    private static readonly XNamespace Avalonia = "https://github.com/avaloniaui";
    private static readonly XNamespace X        = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Sys      = "clr-namespace:System;assembly=System.Runtime";

    public static void Import(string csvPath, string languageCode, string outputDirectory)
    {
        var rows = ParseCsv(csvPath);

        // Group by source file; skip rows with no translation
        var byFile = rows
            .Where(r => !string.IsNullOrEmpty(r.Translation))
            .GroupBy(r => r.File);

        Directory.CreateDirectory(outputDirectory);

        foreach (var group in byFile)
        {
            var fileName = OverlayFileName(group.Key, languageCode);
            var outPath  = Path.Combine(outputDirectory, fileName);
            WriteOverlay(outPath, group, languageCode);
        }
    }

    public static string OverlayFileName(string fileId, string languageCode)
    {
        // "Strings.axaml" + "de" → "Strings.de.axaml"
        var withoutExt = Path.GetFileNameWithoutExtension(fileId);
        return $"{withoutExt}.{languageCode}.axaml";
    }

    /// Tries to extract a BCP-47 language code from a filename like "ui-strings.de.csv".
    /// Returns null if the filename does not contain a recognisable two- or three-letter code.
    public static string? DetectLanguage(string csvPath)
    {
        var name   = Path.GetFileNameWithoutExtension(csvPath); // e.g. "ui-strings.de"
        var dotIdx = name.LastIndexOf('.');
        if (dotIdx < 0) return null;
        var candidate = name[(dotIdx + 1)..];
        // Accept codes like "de", "fr", "pt-BR", "zh-Hans"
        return candidate.Length is >= 2 and <= 8 ? candidate : null;
    }

    private static void WriteOverlay(string path, IEnumerable<CsvRow> rows, string languageCode)
    {
        var root = new XElement(Avalonia + "ResourceDictionary",
            new XAttribute(XNamespace.Xmlns + "x",   X.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "sys",  Sys.NamespaceName));

        // Sentinel key required by LanguageApplier to locate and remove the overlay
        // when the user switches language.
        root.Add(new XElement(Sys + "String",
            new XAttribute(X + "Key", "_LanguageOverlayMarker"),
            languageCode));

        foreach (var row in rows)
        {
            root.Add(new XElement(Sys + "String",
                new XAttribute(X + "Key", row.Key),
                row.Translation));
        }

        var doc = new XDocument(new XDeclaration("1.0", "utf-8", null), root);
        doc.Save(path);
    }

    // ── CSV parsing ───────────────────────────────────────────────────────

    private record CsvRow(string Key, string Source, string Translation, string File);

    private static List<CsvRow> ParseCsv(string path)
    {
        var rows  = new List<CsvRow>();
        var lines = File.ReadAllLines(path, Encoding.UTF8);

        for (int i = 1; i < lines.Length; i++) // skip header
        {
            var fields = SplitLine(lines[i]);
            if (fields.Count < 4) continue;
            rows.Add(new CsvRow(fields[0], fields[1], fields[2], fields[3]));
        }

        return rows;
    }

    private static List<string> SplitLine(string line)
    {
        var fields  = new List<string>();
        var current = new StringBuilder();
        bool inQuote = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuote)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    { current.Append('"'); i++; }
                else if (c == '"')
                    inQuote = false;
                else
                    current.Append(c);
            }
            else
            {
                if      (c == '"')  inQuote = true;
                else if (c == ',')  { fields.Add(current.ToString()); current.Clear(); }
                else                current.Append(c);
            }
        }

        fields.Add(current.ToString().TrimEnd('\r'));
        return fields;
    }
}
