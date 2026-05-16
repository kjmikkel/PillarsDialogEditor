using System.Text;
using System.Xml.Linq;
using DialogEditor.Core.Editing;

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
        var original = File.Exists(path) ? File.ReadAllText(path) : string.Empty;
        if (File.Exists(path))
            File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, Serialize(original, nodes), Encoding.UTF8);
    }
}
