using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe1ConversationParser
{
    public static IReadOnlyList<ConversationNode> ParseFile(string path)
        => ParseXml(File.ReadAllText(path));

    public static IReadOnlyList<ConversationNode> ParseXml(string xml)
    {
        var doc = XDocument.Parse(xml);
        return doc.Descendants("FlowChartNode")
            .Select(ParseNode)
            .ToList();
    }

    private static ConversationNode ParseNode(XElement node)
    {
        var links = node.Element("Links")?.Elements("FlowChartLink")
            .Select(ParseLink)
            .ToList() ?? [];

        var conditions = node.Element("Conditionals")?.Element("Components")
            ?.Elements("ExpressionComponent")
            .Select(ParseCondition)
            .ToList() ?? [];

        var scriptCount =
            (node.Element("OnEnterScripts")?.HasElements == true ? 1 : 0) +
            (node.Element("OnExitScripts")?.HasElements == true ? 1 : 0) +
            (node.Element("OnUpdateScripts")?.HasElements == true ? 1 : 0);

        return new ConversationNode(
            NodeId: (int)node.Element("NodeID")!,
            IsPlayerChoice: (bool)node.Element("IsQuestionNode")!,
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
