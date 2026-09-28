using System.Text;
using System.Xml.Linq;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Localisation;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;

namespace DialogEditor.Core.Serialization;

public static class StringTableSerializer
{
    public static string Serialize(string originalXml, IEnumerable<NodeEditSnapshot> nodes)
    {
        XElement entries;
        XDocument doc;

        if (string.IsNullOrWhiteSpace(originalXml))
        {
            entries = new XElement("Entries");
            doc = new XDocument(new XElement("StringTableFile", entries));
        }
        else
        {
            doc = XDocument.Parse(originalXml);
            entries = doc.Descendants("Entries").First();
        }

        var byId = entries.Elements("Entry")
            .ToDictionary(e => (int)e.Element("ID")!);

        foreach (var node in nodes)
        {
            if (byId.TryGetValue(node.NodeId, out var entry))
            {
                entry.Element("DefaultText")!.Value = node.DefaultText;
                entry.Element("FemaleText")!.Value  = node.FemaleText;
            }
            else
            {
                entries.Add(new XElement("Entry",
                    new XElement("ID",          node.NodeId),
                    new XElement("DefaultText", node.DefaultText),
                    new XElement("FemaleText",  node.FemaleText)));
            }
        }

        return doc.ToString(SaveOptions.None);
    }

    public static void SaveToFile(string path, IEnumerable<NodeEditSnapshot> nodes)
    {
        RefuseUnreadable(path);
        var original = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, Serialize(original, nodes), Encoding.UTF8);
    }

    public static void SaveToFile(string path, IEnumerable<NodeTranslation> translations)
    {
        RefuseUnreadable(path);
        var exists   = File.Exists(path);
        var original = exists ? File.ReadAllText(path) : string.Empty;
        if (exists)
            File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, SerializeTranslations(original, translations), Encoding.UTF8);
    }

    // Checked before the .bak copy and before any write: a damaged file is left exactly as
    // it is, so the storefront's "verify files" can still repair it (issue 119).
    private static void RefuseUnreadable(string path)
    {
        if (!StringTableParser.IsReadableOrAbsent(path)) throw new StringTableUnreadableException(path);
    }

    // Internal (not private) so the opt-in game-data test can round-trip every shipped
    // stringtable through the exact code F5 and the patcher use (issue 117).
    internal static string SerializeTranslations(string originalXml, IEnumerable<NodeTranslation> translations)
    {
        XElement entries;
        XDocument doc;

        if (string.IsNullOrWhiteSpace(originalXml))
        {
            entries = new XElement("Entries");
            doc = new XDocument(new XElement("StringTableFile", entries));
        }
        else
        {
            doc     = XDocument.Parse(originalXml);
            entries = doc.Descendants("Entries").First();
        }

        var byId = entries.Elements("Entry")
            .ToDictionary(e => (int)e.Element("ID")!);

        foreach (var t in translations)
        {
            if (byId.TryGetValue(t.NodeId, out var entry))
            {
                entry.Element("DefaultText")!.Value = t.DefaultText;
                entry.Element("FemaleText")!.Value  = t.FemaleText;
            }
            else
            {
                entries.Add(new XElement("Entry",
                    new XElement("ID",          t.NodeId),
                    new XElement("DefaultText", t.DefaultText),
                    new XElement("FemaleText",  t.FemaleText)));
            }
        }

        return doc.ToString(SaveOptions.None);
    }
}

/// <summary>A stringtable exists but isn't readable XML, so it was not written (issue 119).</summary>
[NotLocalised("Developer diagnostic for the log; the editor, Patch Manager and CLI each report it with their own text")]
public sealed class StringTableUnreadableException(string path)
    : IOException($"Stringtable '{path}' is not readable XML; it was left untouched.")
{
    public string FilePath { get; } = path;
}
