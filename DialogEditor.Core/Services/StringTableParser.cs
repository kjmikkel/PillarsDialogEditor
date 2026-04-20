using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Services;

public static class StringTableParser
{
    public static StringTable Parse(string xml)
    {
        var doc = XDocument.Parse(xml);
        var entries = doc.Descendants("Entry").Select(e => new StringEntry(
            Id: (int)e.Element("ID")!,
            DefaultText: (string?)e.Element("DefaultText") ?? string.Empty,
            FemaleText: (string?)e.Element("FemaleText") ?? string.Empty
        ));
        return new StringTable(entries);
    }

    public static StringTable ParseFile(string path)
        => Parse(File.ReadAllText(path));
}
