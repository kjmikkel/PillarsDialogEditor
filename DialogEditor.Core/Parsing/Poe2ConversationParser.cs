using System.Text.Json.Nodes;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Parsing;

public static class Poe2ConversationParser
{
    private const string NullGuid = "00000000-0000-0000-0000-000000000000";

    private static SpeakerCategory ClassifySpeaker(string? type, string speakerGuid)
    {
        if (type is null) return SpeakerCategory.Npc;
        if (type.Contains("PlayerResponseNode")) return SpeakerCategory.Player;
        if (type.Contains("ScriptNode") || type.Contains("BankNode") || type.Contains("TriggerConversationNode"))
            return SpeakerCategory.Script;
        if (speakerGuid == NullGuid) return SpeakerCategory.Narrator;
        return SpeakerCategory.Npc;
    }

    public static IReadOnlyList<ConversationNode> ParseFile(string path)
        => ParseJson(File.ReadAllText(path));

    public static IReadOnlyList<ConversationNode> ParseJson(string json)
    {
        var root = JsonNode.Parse(json)!;
        var firstConversation = root["Conversations"]![0]!;
        var nodes = firstConversation["Nodes"]!.AsArray();

        return nodes
            .Where(n => (n!["NodeID"]?.GetValue<int>() ?? 0) >= 0)
            .Select(ParseNode)
            .ToList();
    }

    private static ConversationNode ParseNode(JsonNode? node)
    {
        var links = node!["Links"]?.AsArray()
            .Select(ParseLink)
            .ToList() ?? [];

        var conditions = FlattenConditions(node["Conditionals"]?["Components"]?.AsArray());
        var scripts = ParseScripts(node);

        return new ConversationNode(
            NodeId: node["NodeID"]!.GetValue<int>(),
            IsPlayerChoice: node["$type"]?.GetValue<string>()?.Contains("PlayerResponseNode") ?? false,
            SpeakerCategory: ClassifySpeaker(
                node["$type"]?.GetValue<string>(),
                node["SpeakerGuid"]?.GetValue<string>() ?? string.Empty),
            SpeakerGuid: node["SpeakerGuid"]?.GetValue<string>() ?? string.Empty,
            ListenerGuid: node["ListenerGuid"]?.GetValue<string>() ?? string.Empty,
            Links: links,
            ConditionStrings: conditions,
            Scripts: scripts,
            DisplayType: MapDisplayType(node["DisplayType"]?.GetValue<int>() ?? 0),
            Persistence: MapPersistence(node["Persistence"]?.GetValue<int>() ?? 0)
        );
    }

    private static List<string> ParseScripts(JsonNode? node)
    {
        var result = new List<string>();
        AppendScripts(result, node?["OnEnterScripts"]?.AsArray(), "[Enter]");
        AppendScripts(result, node?["OnExitScripts"]?.AsArray(), "[Exit]");
        AppendScripts(result, node?["OnUpdateScripts"]?.AsArray(), "[Update]");
        return result;
    }

    private static void AppendScripts(List<string> result, JsonArray? scripts, string prefix)
    {
        if (scripts is null) return;
        foreach (var s in scripts)
        {
            var fullName = s?["Data"]?["FullName"]?.GetValue<string>() ?? string.Empty;
            var parameters = s?["Data"]?["Parameters"]?.AsArray()
                .Select(p => p!.GetValue<string>())
                .ToList() ?? [];
            result.Add($"{prefix} {ConditionFormatter.FormatScript(fullName, parameters)}");
        }
    }

    private static NodeLink ParseLink(JsonNode? link)
        => new(
            FromNodeId: link!["FromNodeID"]!.GetValue<int>(),
            ToNodeId: link["ToNodeID"]!.GetValue<int>(),
            HasConditions: link["Conditionals"]?["Components"]?.AsArray().Count > 0
        );

    private static List<string> FlattenConditions(JsonArray? components)
    {
        if (components is null) return [];
        return components
            .SelectMany(c => c?["Data"] is not null
                ? (IEnumerable<string>)[ParseCondition(c)]
                : FlattenConditions(c?["Components"]?.AsArray()))
            .ToList();
    }

    private static string ParseCondition(JsonNode? component)
    {
        var data = component!["Data"]!;
        var fullName = data["FullName"]!.GetValue<string>();
        var parameters = data["Parameters"]?.AsArray()
            .Select(p => p!.GetValue<string>())
            .ToList() ?? [];
        var not = component["Not"]?.GetValue<bool>() ?? false;
        return ConditionFormatter.Format(fullName, parameters, not);
    }

    private static string MapDisplayType(int value) => value switch
    {
        0 => "Conversation",
        1 => "Bark",
        _ => $"Unknown({value})"
    };

    private static string MapPersistence(int value) => value switch
    {
        0 => "None",
        1 => "OnceEver",
        _ => $"Unknown({value})"
    };
}
