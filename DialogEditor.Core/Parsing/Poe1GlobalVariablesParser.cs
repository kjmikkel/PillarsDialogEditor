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

    /// Each global's start-of-game value (issue 345), keyed on its Tag. <c>InitialValue</c> is an
    /// int in OEIFormats' GlobalVariablesData; a missing or unreadable one is 0, the int default
    /// the game would deserialise. Tags are case-sensitive, as the game's dictionary is.
    public static IReadOnlyDictionary<string, int> ParseInitialValues(string xml)
    {
        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var v in XDocument.Parse(xml).Descendants("GlobalVariable"))
        {
            var tag = (string?)v.Element("Tag");
            if (string.IsNullOrWhiteSpace(tag)) continue;
            values[tag] = int.TryParse((string?)v.Element("InitialValue"), out var value) ? value : 0;
        }
        return values;
    }

    public static IReadOnlyDictionary<string, int> ParseInitialValuesFile(string path)
    {
        if (!File.Exists(path)) return new Dictionary<string, int>();
        return ParseInitialValues(File.ReadAllText(path, System.Text.Encoding.UTF8));
    }
}
