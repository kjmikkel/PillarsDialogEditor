using System.Text;
using System.Xml.Linq;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

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

    private static string XsiType(NodeEditSnapshot snap) => snap.SpeakerCategory switch
    {
        SpeakerCategory.Player => "PlayerResponseNode",
        SpeakerCategory.Script => "ScriptNode",
        _                      => "TalkNode",
    };

    private static void ApplyNodeSnapshot(XElement node, NodeEditSnapshot snap)
    {
        node.SetAttributeValue(Xsi + "type", XsiType(snap));
        SetOrAdd(node, "SpeakerGuid",    snap.SpeakerGuid);
        SetOrAdd(node, "ListenerGuid",   snap.ListenerGuid);
        SetOrAdd(node, "DisplayType",    snap.DisplayType);
        SetOrAdd(node, "Persistence",    snap.Persistence);
        SetOrAdd(node, "ActorDirection", snap.ActorDirection);
        SetOrAdd(node, "Comments",       snap.Comments);
        SetOrAdd(node, "VOFilename",     snap.ExternalVO);

        // Update script lists from snapshot
        ReplaceScripts(node, "OnEnterScripts",  snap.Scripts, ScriptCategory.Enter);
        ReplaceScripts(node, "OnExitScripts",   snap.Scripts, ScriptCategory.Exit);
        ReplaceScripts(node, "OnUpdateScripts", snap.Scripts, ScriptCategory.Update);

        // Update condition tree from snapshot
        var condElem = node.Element("Conditionals") ?? new XElement("Conditionals");
        if (node.Element("Conditionals") is null) node.Add(condElem);
        var compElem = condElem.Element("Components") ?? new XElement("Components");
        if (condElem.Element("Components") is null) condElem.Add(compElem);
        compElem.RemoveAll();
        foreach (var c in snap.Conditions) compElem.Add(BuildConditionXml(c));

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
                // Update link conditions when the snapshot carries them
                if (link.Conditions is { Count: >= 0 })
                {
                    var lc = orig.Element("Conditionals") ?? new XElement("Conditionals");
                    if (orig.Element("Conditionals") is null) orig.Add(lc);
                    var lcc = lc.Element("Components") ?? new XElement("Components");
                    if (lc.Element("Components") is null) lc.Add(lcc);
                    lcc.RemoveAll();
                    foreach (var c in link.Conditions) lcc.Add(BuildConditionXml(c));
                }
                linksElem.Add(orig);
            }
            else
            {
                linksElem.Add(BuildNewLink(link));
            }
        }
    }

    private static XElement BuildNewNode(NodeEditSnapshot snap) => new("FlowChartNode",
        new XAttribute(Xsi + "type", XsiType(snap)),
        new XElement("NodeID",       snap.NodeId),
        new XElement("SpeakerGuid",  snap.SpeakerGuid),
        new XElement("ListenerGuid", snap.ListenerGuid),
        // No original XML to merge with — every link on a brand-new node is new.
        new XElement("Links", snap.Links.Select(BuildNewLink)),
        new XElement("Conditionals",
            new XElement("Components",
                snap.Conditions.Select(c => BuildConditionXml(c)))),
        BuildScriptListXml("OnEnterScripts",  snap.Scripts, ScriptCategory.Enter),
        BuildScriptListXml("OnExitScripts",   snap.Scripts, ScriptCategory.Exit),
        BuildScriptListXml("OnUpdateScripts", snap.Scripts, ScriptCategory.Update),
        new XElement("DisplayType",    snap.DisplayType),
        new XElement("Persistence",    snap.Persistence),
        new XElement("ActorDirection", snap.ActorDirection),
        new XElement("Comments",       snap.Comments),
        new XElement("VOFilename",     snap.ExternalVO));

    private static XElement BuildConditionXml(ConditionNode node)
    {
        if (node is ConditionLeaf leaf)
        {
            return new XElement("ExpressionComponent",
                new XAttribute(Xsi + "type", "ConditionalCall"),
                new XElement("Data",
                    new XElement("FullName", leaf.FullName),
                    new XElement("Parameters",
                        leaf.Parameters.Select(p => new XElement("string", p)))),
                new XElement("Not",      leaf.Not),
                new XElement("Operator", leaf.Operator));
        }

        var branch = (ConditionBranch)node;
        return new XElement("ExpressionComponent",
            new XAttribute(Xsi + "type", "ConditionalExpression"),
            new XElement("Components",
                branch.Components.Select(c => BuildConditionXml(c))),
            new XElement("Not",      branch.Not),
            new XElement("Operator", branch.Operator));
    }

    private static XElement BuildNewLink(LinkEditSnapshot link) => new("FlowChartLink",
        new XElement("FromNodeID",              link.FromNodeId),
        new XElement("ToNodeID",                link.ToNodeId),
        new XElement("RandomWeight",            link.RandomWeight),
        new XElement("QuestionNodeTextDisplay", link.QuestionNodeTextDisplay),
        new XElement("Conditionals",
            new XElement("Components",
                (link.Conditions ?? []).Select(c => BuildConditionXml(c)))));

    private static XElement BuildScriptListXml(
        string elementName,
        IReadOnlyList<ScriptCall> scripts,
        ScriptCategory category)
    {
        var calls = scripts.Where(s => s.Category == category)
            .Select(s => new XElement("ScriptCall",
                new XAttribute(Xsi + "type", "ConditionalCall"),
                new XElement("Data",
                    new XElement("FullName", s.FullName),
                    new XElement("Parameters",
                        s.Parameters.Select(p => new XElement("string", p))))));
        return new XElement(elementName, calls);
    }

    private static void ReplaceScripts(
        XElement node,
        string elementName,
        IReadOnlyList<ScriptCall> scripts,
        ScriptCategory category)
    {
        var elem = node.Element(elementName);
        if (elem is null) { node.Add(BuildScriptListXml(elementName, scripts, category)); return; }
        elem.RemoveAll();
        foreach (var s in scripts.Where(sc => sc.Category == category))
            elem.Add(new XElement("ScriptCall",
                new XAttribute(Xsi + "type", "ConditionalCall"),
                new XElement("Data",
                    new XElement("FullName", s.FullName),
                    new XElement("Parameters",
                        s.Parameters.Select(p => new XElement("string", p))))));
    }

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
