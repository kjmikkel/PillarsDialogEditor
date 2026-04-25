using System.Text;
using System.Xml.Linq;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe1ConversationParser
{
    public static IReadOnlyList<ConversationNode> ParseFile(string path)
        => ParseXml(File.ReadAllText(path));

    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

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

        var conditions = FlattenConditions(
            node.Element("Conditionals")?.Element("Components"));

        var scripts = ParseScripts(node);

        var nodeType = (string?)node.Attribute(Xsi + "type") ?? "";
        return new ConversationNode(
            NodeId: (int)node.Element("NodeID")!,
            IsPlayerChoice: nodeType == "PlayerResponseNode",
            SpeakerCategory: ClassifySpeaker(
                nodeType,
                (string?)node.Element("SpeakerGuid") ?? string.Empty),
            SpeakerGuid: (string?)node.Element("SpeakerGuid") ?? string.Empty,
            ListenerGuid: (string?)node.Element("ListenerGuid") ?? string.Empty,
            Links: links,
            ConditionStrings: conditions,
            Scripts: scripts,
            DisplayType: (string?)node.Element("DisplayType") ?? string.Empty,
            Persistence: (string?)node.Element("Persistence") ?? string.Empty,
            ActorDirection: (string?)node.Element("ActorDirection") ?? string.Empty,
            Comments: (string?)node.Element("Comments") ?? string.Empty,
            ExternalVO: (string?)node.Element("VOFilename") ?? string.Empty,
            ConditionExpression: FormatConditionTree(
                node.Element("Conditionals")?.Element("Components"))
        );
    }

    private static NodeLink ParseLink(XElement link)
        => new(
            FromNodeId: (int)link.Element("FromNodeID")!,
            ToNodeId: (int)link.Element("ToNodeID")!,
            HasConditions: link.Element("Conditionals")?.Element("Components")?.HasElements == true,
            RandomWeight: (float?)link.Element("RandomWeight") ?? 1f,
            QuestionNodeTextDisplay: (string?)link.Element("QuestionNodeTextDisplay") ?? string.Empty
        );

    private static List<string> ParseScripts(XElement node)
    {
        var result = new List<string>();
        AppendScripts(result, node.Element("OnEnterScripts"), "[Enter]");
        AppendScripts(result, node.Element("OnExitScripts"), "[Exit]");
        AppendScripts(result, node.Element("OnUpdateScripts"), "[Update]");
        return result;
    }

    private static void AppendScripts(List<string> result, XElement? scriptList, string prefix)
    {
        if (scriptList is null) return;
        foreach (var entry in scriptList.Elements())
        {
            var data = entry.Element("Data");
            if (data is null) continue;
            var fullName = (string?)data.Element("FullName") ?? string.Empty;
            var parameters = data.Element("Parameters")?.Elements("string")
                .Select(e => (string)e)
                .ToList() ?? [];
            result.Add($"{prefix} {ConditionFormatter.FormatScript(fullName, parameters)}");
        }
    }

    private static List<string> FlattenConditions(XElement? components)
    {
        if (components is null) return [];
        return components.Elements("ExpressionComponent")
            .SelectMany(c => c.Element("Data") is not null
                ? (IEnumerable<string>)[ParseCondition(c)]
                : FlattenConditions(c.Element("Components")))
            .ToList();
    }

    private static SpeakerCategory ClassifySpeaker(string nodeType, string speakerGuid) =>
        nodeType switch
        {
            "PlayerResponseNode" => SpeakerCategory.Player,
            "ScriptNode" or "BankNode" or "TriggerConversationNode" => SpeakerCategory.Script,
            _ => speakerGuid == "00000000-0000-0000-0000-000000000000"
                ? SpeakerCategory.Narrator
                : SpeakerCategory.Npc
        };

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

    private static string FormatConditionTree(XElement? components, int depth = 0)
    {
        if (components is null) return string.Empty;
        var indent = new string(' ', depth * 2);
        var sb = new StringBuilder();
        var items = components.Elements("ExpressionComponent").ToList();

        for (int i = 0; i < items.Count; i++)
        {
            var comp = items[i];
            var not = (bool?)comp.Element("Not") ?? false;
            var notPrefix = not ? "NOT " : "";

            if (i > 0)
            {
                var prevOp = ((string?)items[i - 1].Element("Operator") ?? "And").ToUpperInvariant();
                sb.Append($"{Environment.NewLine}{indent}{prevOp} ");
            }

            if (comp.Element("Data") is not null)
            {
                // ParseCondition already handles Not internally
                sb.Append(ParseCondition(comp));
            }
            else
            {
                var inner = FormatConditionTree(comp.Element("Components"), depth + 1);
                if (string.IsNullOrEmpty(inner)) continue;
                sb.Append($"{notPrefix}({Environment.NewLine}{indent}  {inner}{Environment.NewLine}{indent})");
            }
        }
        return sb.ToString();
    }
}
