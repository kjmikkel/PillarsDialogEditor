using System.Text;
using System.Xml.Linq;
using DialogEditor.Core.Models;
using DialogEditor.Core.Resources;

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

        var conditions = ParseConditionTree(
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
            Conditions: conditions,
            Scripts: scripts,
            DisplayType: (string?)node.Element("DisplayType") ?? string.Empty,
            Persistence: (string?)node.Element("Persistence") ?? string.Empty,
            ActorDirection: (string?)node.Element("ActorDirection") ?? string.Empty,
            Comments: (string?)node.Element("Comments") ?? string.Empty,
            ExternalVO: (string?)node.Element("VOFilename") ?? string.Empty
        );
    }

    private static NodeLink ParseLink(XElement link)
        => new(
            FromNodeId: (int)link.Element("FromNodeID")!,
            ToNodeId: (int)link.Element("ToNodeID")!,
            Conditions: ParseConditionTree(link.Element("Conditionals")?.Element("Components")),
            RandomWeight: (float?)link.Element("RandomWeight") ?? 1f,
            QuestionNodeTextDisplay: (string?)link.Element("QuestionNodeTextDisplay") ?? string.Empty
        );

    private static List<ScriptCall> ParseScripts(XElement node)
    {
        var result = new List<ScriptCall>();
        ReadScripts(result, node.Element("OnEnterScripts"),  ScriptCategory.Enter);
        ReadScripts(result, node.Element("OnExitScripts"),   ScriptCategory.Exit);
        ReadScripts(result, node.Element("OnUpdateScripts"), ScriptCategory.Update);
        return result;
    }

    private static void ReadScripts(List<ScriptCall> result, XElement? scriptList, ScriptCategory category)
    {
        if (scriptList is null) return;
        foreach (var entry in scriptList.Elements())
        {
            var data = entry.Element("Data");
            if (data is null) continue;
            var fullName   = (string?)data.Element("FullName") ?? string.Empty;
            var parameters = data.Element("Parameters")?.Elements("string")
                .Select(e => (string)e)
                .ToList() ?? [];
            result.Add(new ScriptCall(fullName, parameters, category));
        }
    }

    internal static IReadOnlyList<ConditionNode> ParseConditionTree(XElement? components)
    {
        if (components is null) return [];
        return components.Elements("ExpressionComponent")
            .Select(ParseConditionComponent)
            .ToList();
    }

    private static ConditionNode ParseConditionComponent(XElement comp)
    {
        var not      = (bool?)comp.Element("Not") ?? false;
        var op       = (string?)comp.Element("Operator") ?? "And";
        var data     = comp.Element("Data");

        if (data is not null)
        {
            var fullName   = (string)data.Element("FullName")!;
            var parameters = data.Element("Parameters")?.Elements("string")
                .Select(e => (string)e)
                .ToList() ?? [];
            return new ConditionLeaf(fullName, parameters, not, op);
        }

        // Nested group
        var children = ParseConditionTree(comp.Element("Components"));
        return new ConditionBranch(children, not, op);
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

}
