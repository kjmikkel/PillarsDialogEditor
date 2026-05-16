using System.Text;
using System.Xml.Linq;
using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Serialization;

public static class Poe1ConversationSerializer
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    public static string Serialize(string originalXml, ConversationEditSnapshot snapshot)
    {
        var doc   = XDocument.Parse(originalXml);
        var nodes = doc.Descendants("Nodes").First();

        var origByNodeId = nodes.Elements("FlowChartNode")
            .ToDictionary(e => (int)e.Element("NodeID")!);

        var snapshotIds = snapshot.Nodes.Select(n => n.NodeId).ToHashSet();

        foreach (var id in origByNodeId.Keys.Where(id => !snapshotIds.Contains(id)).ToList())
            origByNodeId[id].Remove();

        foreach (var snap in snapshot.Nodes)
        {
            if (origByNodeId.TryGetValue(snap.NodeId, out var elem))
                ApplyNodeSnapshot(elem, snap);
            else
                nodes.Add(BuildNewNode(snap));
        }

        return doc.ToString(SaveOptions.None);
    }

    private static void ApplyNodeSnapshot(XElement node, NodeEditSnapshot snap)
    {
        node.SetAttributeValue(Xsi + "type", snap.IsPlayerChoice ? "PlayerResponseNode" : "TalkNode");
        SetOrAdd(node, "SpeakerGuid",    snap.SpeakerGuid);
        SetOrAdd(node, "ListenerGuid",   snap.ListenerGuid);
        SetOrAdd(node, "DisplayType",    snap.DisplayType);
        SetOrAdd(node, "Persistence",    snap.Persistence);
        SetOrAdd(node, "ActorDirection", snap.ActorDirection);
        SetOrAdd(node, "Comments",       snap.Comments);
        SetOrAdd(node, "VOFilename",     snap.ExternalVO);

        var linksElem = node.Element("Links") ?? new XElement("Links");
        if (node.Element("Links") is null) node.Add(linksElem);
        var origLinks = linksElem.Elements("FlowChartLink").ToList();
        linksElem.RemoveAll();

        foreach (var link in snap.Links)
        {
            var orig = origLinks.FirstOrDefault(l =>
                (int)l.Element("FromNodeID")! == link.FromNodeId &&
                (int)l.Element("ToNodeID")!   == link.ToNodeId);

            if (orig is not null)
            {
                orig.Element("RandomWeight")!.Value            = link.RandomWeight.ToString();
                orig.Element("QuestionNodeTextDisplay")!.Value = link.QuestionNodeTextDisplay;
                linksElem.Add(orig);
            }
            else
            {
                linksElem.Add(BuildNewLink(link));
            }
        }
    }

    private static XElement BuildNewNode(NodeEditSnapshot snap) => new("FlowChartNode",
        new XAttribute(Xsi + "type", snap.IsPlayerChoice ? "PlayerResponseNode" : "TalkNode"),
        new XElement("NodeID",       snap.NodeId),
        new XElement("SpeakerGuid",  snap.SpeakerGuid),
        new XElement("ListenerGuid", snap.ListenerGuid),
        new XElement("Links"),
        new XElement("Conditionals", new XElement("Components")),
        new XElement("OnEnterScripts"),
        new XElement("OnExitScripts"),
        new XElement("OnUpdateScripts"),
        new XElement("DisplayType",    snap.DisplayType),
        new XElement("Persistence",    snap.Persistence),
        new XElement("ActorDirection", snap.ActorDirection),
        new XElement("Comments",       snap.Comments),
        new XElement("VOFilename",     snap.ExternalVO));

    private static XElement BuildNewLink(LinkEditSnapshot link) => new("FlowChartLink",
        new XElement("FromNodeID",              link.FromNodeId),
        new XElement("ToNodeID",                link.ToNodeId),
        new XElement("RandomWeight",            link.RandomWeight),
        new XElement("QuestionNodeTextDisplay", link.QuestionNodeTextDisplay),
        new XElement("Conditionals",            new XElement("Components")));

    private static void SetOrAdd(XElement parent, string name, string value)
    {
        var elem = parent.Element(name);
        if (elem is not null) elem.Value = value;
        else parent.Add(new XElement(name, value));
    }

    public static void SaveToFile(string path, ConversationEditSnapshot snapshot)
    {
        var original = File.ReadAllText(path);
        File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, Serialize(original, snapshot), Encoding.UTF8);
    }
}
