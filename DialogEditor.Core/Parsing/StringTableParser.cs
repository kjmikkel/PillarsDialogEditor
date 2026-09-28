using System.Xml;
using System.Xml.Linq;
using DialogEditor.Core.Logging;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

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

    /// <summary>
    /// Loads a conversation's stringtable for display: <see cref="StringTable.Missing"/> when
    /// the file doesn't exist, <see cref="StringTable.Unreadable"/> when it isn't XML.
    /// </summary>
    /// <remarks>
    /// One damaged text file (a GOG Deadfire install ships a binary German stringtable, issue 119)
    /// must not make the whole conversation unopenable in that language: its structure still
    /// loads, and the text shows as missing.
    /// </remarks>
    public static StringTable LoadFile(string path)
    {
        if (!File.Exists(path)) return StringTable.Missing;
        try
        {
            return ParseFile(path);
        }
        catch (XmlException ex)
        {
            AppLog.Warn($"Stringtable '{path}' is not readable XML and is shown as missing: {ex.Message}");
            return StringTable.Unreadable(path);
        }
    }

    /// <summary>True when <paramref name="path"/> is absent or parses as XML — i.e. safe to
    /// write through. A damaged file must be refused, never overwritten (issue 119).</summary>
    public static bool IsReadableOrAbsent(string path)
    {
        if (!File.Exists(path)) return true;
        try
        {
            XDocument.Parse(File.ReadAllText(path));
            return true;
        }
        catch (XmlException ex)
        {
            AppLog.Warn($"Stringtable '{path}' is not readable XML: {ex.Message}");
            return false;
        }
    }
}
