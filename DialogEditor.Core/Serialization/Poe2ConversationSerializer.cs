using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;

namespace DialogEditor.Core.Serialization;

public static class Poe2ConversationSerializer
{
    private const string TalkNodeType     = "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats";
    private const string PlayerNodeType   = "OEIFormats.FlowCharts.Conversations.PlayerResponseNode, OEIFormats";
    private const string ScriptNodeType   = "OEIFormats.FlowCharts.Conversations.ScriptNode, OEIFormats";
    private const string DialogueLinkType = "OEIFormats.FlowCharts.Conversations.DialogueLink, OEIFormats";

    private static string NodeType(NodeEditSnapshot snap) => snap.SpeakerCategory switch
    {
        SpeakerCategory.Player => PlayerNodeType,
        SpeakerCategory.Script => ScriptNodeType,
        _                      => TalkNodeType,
    };

    public static string Serialize(string originalJson, ConversationEditSnapshot snapshot)
    {
        var root    = JsonNode.Parse(originalJson)!;
        var conv    = root["Conversations"]![0]!;
        var origArr = conv["Nodes"]!.AsArray();

        var origByNodeId = origArr
            .Where(n => n!["NodeID"] is not null)
            .ToDictionary(n => n!["NodeID"]!.GetValue<int>(), n => n!);

        var newArr = new JsonArray();

        foreach (var nodeSnap in snapshot.Nodes)
        {
            if (origByNodeId.TryGetValue(nodeSnap.NodeId, out var orig))
            {
                var updated = JsonNode.Parse(orig.ToJsonString())!;
                ApplyNodeSnapshot(updated, nodeSnap, orig);
                newArr.Add(updated);
            }
            else
            {
                newArr.Add(BuildNewNode(nodeSnap));
            }
        }

        conv["Nodes"] = newArr;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void ApplyNodeSnapshot(JsonNode node, NodeEditSnapshot snap, JsonNode original)
    {
        node["$type"]        = NodeType(snap);
        node["SpeakerGuid"]  = snap.SpeakerGuid;
        node["ListenerGuid"] = snap.ListenerGuid;
        node["DisplayType"]  = MapDisplayType(snap.DisplayType);
        node["Persistence"]  = MapPersistence(snap.Persistence);
        node["HideSpeaker"]  = snap.HideSpeaker;
        node["HasVO"]        = snap.HasVO;
        node["ExternalVO"]   = snap.ExternalVO;
        node["Links"]            = BuildLinks(snap.Links, original["Links"]?.AsArray());
        node["Conditionals"]     = BuildConditionJson(snap.Conditions);
        node["OnEnterScripts"]   = BuildScriptListJson(snap.Scripts, ScriptCategory.Enter);
        node["OnExitScripts"]    = BuildScriptListJson(snap.Scripts, ScriptCategory.Exit);
        node["OnUpdateScripts"]  = BuildScriptListJson(snap.Scripts, ScriptCategory.Update);
    }

    private static JsonArray BuildLinks(IReadOnlyList<LinkEditSnapshot> links, JsonArray? originalLinks)
    {
        var arr = new JsonArray();
        foreach (var link in links)
        {
            var orig = originalLinks?.FirstOrDefault(l =>
                l!["FromNodeID"]?.GetValue<int>() == link.FromNodeId &&
                l["ToNodeID"]?.GetValue<int>()    == link.ToNodeId);

            if (orig is not null)
            {
                var cloned = JsonNode.Parse(orig.ToJsonString())!;
                cloned["RandomWeight"]            = link.RandomWeight;
                cloned["QuestionNodeTextDisplay"] = MapQuestionDisplay(link.QuestionNodeTextDisplay);
                // Update link conditions when the snapshot carries them
                if (link.Conditions is { Count: >= 0 })
                    cloned["Conditionals"] = BuildConditionJson(link.Conditions);
                arr.Add(cloned);
            }
            else
            {
                arr.Add(BuildNewLink(link));
            }
        }
        return arr;
    }

    private static JsonNode BuildNewNode(NodeEditSnapshot snap)
    {
        var node = BuildNewNodeBase(snap);
        node["Conditionals"]    = BuildConditionJson(snap.Conditions);
        node["OnEnterScripts"]  = BuildScriptListJson(snap.Scripts, ScriptCategory.Enter);
        node["OnExitScripts"]   = BuildScriptListJson(snap.Scripts, ScriptCategory.Exit);
        node["OnUpdateScripts"] = BuildScriptListJson(snap.Scripts, ScriptCategory.Update);
        return node;
    }

    private static JsonArray BuildScriptListJson(
        IReadOnlyList<ScriptCall> scripts,
        ScriptCategory category)
    {
        var arr = new JsonArray();
        foreach (var s in scripts.Where(sc => sc.Category == category))
        {
            var parameters = new JsonArray();
            foreach (var p in s.Parameters) parameters.Add(JsonValue.Create(p));
            arr.Add(new JsonObject
            {
                ["Data"] = new JsonObject
                {
                    ["FullName"]   = s.FullName,
                    ["Parameters"] = parameters,
                },
            });
        }
        return arr;
    }

    private static JsonNode BuildNewNodeBase(NodeEditSnapshot snap) => JsonNode.Parse($$"""
        {
          "$type": "{{NodeType(snap)}}",
          "SpeakerGuid":  "{{snap.SpeakerGuid}}",
          "ListenerGuid": "{{snap.ListenerGuid}}",
          "IsQuestionNode": false,
          "DisplayType": {{MapDisplayType(snap.DisplayType)}},
          "Persistence": {{MapPersistence(snap.Persistence)}},
          "NodeID": {{snap.NodeId}},
          "ContainerNodeID": -1,
          "Links": [],
          "ClassExtender": {"ExtendedProperties": []},
          "Conditionals": {"Operator": 0, "Components": []},
          "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
          "HideSpeaker": {{snap.HideSpeaker.ToString().ToLower()}},
          "HasVO": {{snap.HasVO.ToString().ToLower()}},
          "ExternalVO": "{{snap.ExternalVO}}",
          "IsTempText": false, "PlayVOAs3DSound": false, "PlayType": 0,
          "NoPlayRandomWeight": 0, "VOPositioning": 0, "NotSkippable": false
        }
        """)!;

    private static JsonNode BuildNewLink(LinkEditSnapshot link)
    {
        var node = JsonNode.Parse($$"""
            {
              "$type": "{{DialogueLinkType}}",
              "FromNodeID": {{link.FromNodeId}},
              "ToNodeID": {{link.ToNodeId}},
              "PointsToGhost": false,
              "Conditionals": {"Operator": 0, "Components": []},
              "ClassExtender": {"ExtendedProperties": []},
              "RandomWeight": {{link.RandomWeight}},
              "PlayQuestionNodeVO": true,
              "QuestionNodeTextDisplay": {{MapQuestionDisplay(link.QuestionNodeTextDisplay)}}
            }
            """)!;
        if (link.Conditions is { Count: > 0 })
            node["Conditionals"] = BuildConditionJson(link.Conditions);
        return node;
    }

    private static JsonNode BuildConditionJson(IReadOnlyList<ConditionNode> conditions)
    {
        var components = new JsonArray();
        foreach (var c in conditions)
            components.Add(BuildConditionComponentJson(c));
        return new JsonObject { ["Operator"] = 0, ["Components"] = components };
    }

    private static JsonNode BuildConditionComponentJson(ConditionNode node)
    {
        if (node is ConditionLeaf leaf)
        {
            var parameters = new JsonArray();
            foreach (var p in leaf.Parameters) parameters.Add(JsonValue.Create(p));
            return new JsonObject
            {
                ["$type"] = "OEIFormats.FlowCharts.ConditionalCall, OEIFormats",
                ["Data"]  = new JsonObject
                {
                    ["FullName"]   = leaf.FullName,
                    ["Parameters"] = parameters,
                },
                ["Not"]      = leaf.Not,
                ["Operator"] = leaf.Operator == "Or" ? 1 : 0,
            };
        }
        var branch     = (ConditionBranch)node;
        var childComps = new JsonArray();
        foreach (var c in branch.Components) childComps.Add(BuildConditionComponentJson(c));
        return new JsonObject
        {
            ["$type"]      = "OEIFormats.FlowCharts.ConditionalExpression, OEIFormats",
            ["Components"] = childComps,
            ["Not"]        = branch.Not,
            ["Operator"]   = branch.Operator == "Or" ? 1 : 0,
        };
    }

    private static int MapDisplayType(string s)     => s == "Bark"     ? 1 : 0;
    private static int MapPersistence(string s)     => s == "OnceEver" ? 1 : 0;
    private static int MapQuestionDisplay(string s) => s switch
    {
        "Always" => 1,
        "Never"  => 2,
        _        => 0
    };

    public static void SaveToFile(string path, ConversationEditSnapshot snapshot)
    {
        var original = File.ReadAllText(path);
        File.Copy(path, path + ".bak", overwrite: true);
        File.WriteAllText(path, Serialize(original, snapshot), Encoding.UTF8);
    }
}
