using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe1GlobalVariablesParser
{
    public static IReadOnlyList<GameDataEntry> Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants("GlobalVariable")
            .Select(v => (string?)v.Element("Tag"))
            .Where(tag => !string.IsNullOrWhiteSpace(tag))
            .OrderBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Select(tag => new GameDataEntry(Id: string.Empty, Name: tag!))
            .ToList();
    }

    public static IReadOnlyList<GameDataEntry> ParseFile(string path)
    {
        if (!File.Exists(path)) return [];
        return Parse(File.ReadAllText(path, System.Text.Encoding.UTF8));
    }
}
