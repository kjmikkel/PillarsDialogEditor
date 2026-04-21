using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe1ConversationParser
{
    public static IReadOnlyList<ConversationNode> ParseFile(string path)
        => ParseXml(File.ReadAllText(path));

    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private static readonly HashSet<string> DialogNodeTypes =
        ["TalkNode", "PlayerResponseNode"];

    public static IReadOnlyList<ConversationNode> ParseXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants("FlowChartNode")
            .Where(n => DialogNodeTypes.Contains((string?)n.Attribute(Xsi + "type") ?? ""))
            .Select(ParseNode)
            .ToList();
    }

    private static ConversationNode ParseNode(XElement node)
    {
        var links = node.Element("Links")?.Elements("FlowChartLink")
            .Select(ParseLink)
            .ToList() ?? [];

        var conditions = FlattenConditions(
            node.Element("Conditionals")?.Element("Components"));

        var scriptCount =
            (node.Element("OnEnterScripts")?.HasElements == true ? 1 : 0) +
            (node.Element("OnExitScripts")?.HasElements == true ? 1 : 0) +
            (node.Element("OnUpdateScripts")?.HasElements == true ? 1 : 0);

        var nodeType = (string?)node.Attribute(Xsi + "type") ?? "";
        return new ConversationNode(
            NodeId: (int)node.Element("NodeID")!,
            IsPlayerChoice: nodeType == "PlayerResponseNode",
            SpeakerGuid: (string?)node.Element("SpeakerGuid") ?? string.Empty,
            ListenerGuid: (string?)node.Element("ListenerGuid") ?? string.Empty,
            Links: links,
            ConditionStrings: conditions,
            ScriptCount: scriptCount,
            DisplayType: (string?)node.Element("DisplayType") ?? string.Empty,
            Persistence: (string?)node.Element("Persistence") ?? string.Empty
        );
    }

    private static NodeLink ParseLink(XElement link)
        => new(
            FromNodeId: (int)link.Element("FromNodeID")!,
            ToNodeId: (int)link.Element("ToNodeID")!,
            HasConditions: link.Element("Conditionals")?.Element("Components")?.HasElements == true
        );

    private static List<string> FlattenConditions(XElement? components)
    {
        if (components is null) return [];
        return components.Elements("ExpressionComponent")
            .SelectMany(c => c.Element("Data") is not null
                ? (IEnumerable<string>)[ParseCondition(c)]
                : FlattenConditions(c.Element("Components")))
            .ToList();
    }

    private static string ParseCondition(XElement component)
    {
        var data = component.Element("Data")!;
        var fullName = (string)data.Element("FullName")!;
        var parameters = data.Element("Parameters")?.Elements("string")
            .Select(e => (string)e)
            .ToList() ?? [];
        var not = (bool?)component.Element("Not") ?? false;
        return ConditionFormatter.Format(fullName, parameters, not);
    }
}
