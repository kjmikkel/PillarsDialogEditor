using System.Text;
using System.Xml.Linq;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Exports UI strings from AXAML resource dictionaries to a CSV file ready for translation.
/// CSV columns: Key, Source, Translation (blank), File.
/// </summary>
public static class UiStringExportService
{
    private static readonly XNamespace X   = "http://schemas.microsoft.com/winfx/2006/xaml";
    private static readonly XNamespace Sys = "clr-namespace:System;assembly=System.Runtime";

    /// <param name="sourceFiles">
    /// Pairs of (logical file ID, path to the AXAML file on disk).
    /// The file ID (e.g. "Strings.axaml") is written into the File column.
    /// </param>
    public static void Export(
        IEnumerable<(string FileId, string AxamlPath)> sourceFiles,
        string outputPath)
    {
        Export(sourceFiles.Select(f => (f.FileId, (Stream)File.OpenRead(f.AxamlPath))), outputPath);
    }

    /// <param name="sourceStreams">
    /// Pairs of (logical file ID, stream of AXAML content — caller disposes).
    /// Use this overload when reading from embedded Avalonia assets via AssetLoader.
    /// </param>
    public static void Export(
        IEnumerable<(string FileId, Stream Content)> sourceStreams,
        string outputPath)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Key,Source,Translation,File");

        foreach (var (fileId, stream) in sourceStreams)
        {
            var doc = XDocument.Load(stream);
            foreach (var el in doc.Root!.Elements(Sys + "String"))
            {
                var key    = el.Attribute(X + "Key")?.Value;
                var source = el.Value;
                if (key is null) continue;

                sb.Append(CsvField(key));
                sb.Append(',');
                sb.Append(CsvField(source));
                sb.Append(',');
                sb.Append(','); // Translation — empty placeholder
                sb.AppendLine(CsvField(fileId));
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(outputPath) ?? ".");
        File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
    }

    private static string CsvField(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return '"' + value.Replace("\"", "\"\"") + '"';
        return value;
    }
}
