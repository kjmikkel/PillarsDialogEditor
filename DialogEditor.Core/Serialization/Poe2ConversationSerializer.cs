using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;

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

        var snapById = new Dictionary<int, NodeEditSnapshot>();
        foreach (var s in snapshot.Nodes) snapById.TryAdd(s.NodeId, s);

        // Walk the original array so every node keeps its position (issue 115). Nodes the
        // parser never shows — NodeID -200, the conversation-level ScriptNode present in
        // every bundle — are copied verbatim; the snapshot cannot express them, so it
        // cannot have deleted them. Existing nodes missing from the snapshot were deleted.
        var newArr      = new JsonArray();
        var originalIds = new HashSet<int>();
        foreach (var orig in origArr)
        {
            if (orig is null) continue;
            if (IsHiddenFromEditor(orig)) { newArr.Add(orig.DeepClone()); continue; }

            var id = orig["NodeID"]!.GetValue<int>();
            originalIds.Add(id);
            if (!snapById.TryGetValue(id, out var nodeSnap)) continue;

            var updated = orig.DeepClone();
            ApplyNodeSnapshot(updated, nodeSnap, orig);
            newArr.Add(updated);
        }

        foreach (var nodeSnap in snapshot.Nodes.Where(s => !originalIds.Contains(s.NodeId)))
            newArr.Add(BuildNewNode(nodeSnap));

        conv["Nodes"] = newArr;
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static void ApplyNodeSnapshot(JsonNode node, NodeEditSnapshot snap, JsonNode original)
    {
        node["$type"]        = NodeTypeFamily.Resolve(
            original["$type"]?.GetValue<string>(), snap.SpeakerCategory, NodeType(snap));
        node["SpeakerGuid"]  = snap.SpeakerGuid;
        node["ListenerGuid"] = snap.ListenerGuid;
        SetEnumOrRemove(node, "DisplayType", Poe2EnumMaps.DisplayTypeValue(snap.DisplayType));
        SetEnumOrRemove(node, "Persistence", Poe2EnumMaps.PersistenceValue(snap.Persistence));
        node["HideSpeaker"]  = snap.HideSpeaker;
        node["HasVO"]        = snap.HasVO;
        node["ExternalVO"]   = snap.ExternalVO;
        node["Links"]            = BuildLinks(snap.Links, original["Links"]?.AsArray());
        node["Conditionals"]     = BuildConditionJson(snap.Conditions, original["Conditionals"]);
        node["OnEnterScripts"]   = BuildScriptListJson(snap.Scripts, ScriptCategory.Enter,  original["OnEnterScripts"]?.AsArray());
        node["OnExitScripts"]    = BuildScriptListJson(snap.Scripts, ScriptCategory.Exit,   original["OnExitScripts"]?.AsArray());
        node["OnUpdateScripts"]  = BuildScriptListJson(snap.Scripts, ScriptCategory.Update, original["OnUpdateScripts"]?.AsArray());
    }

    // Mirrors Poe2ConversationParser.ParseJson, which only surfaces NodeID >= 0.
    private static bool IsHiddenFromEditor(JsonNode node) =>
        node["NodeID"] is not { } id || id.GetValue<int>() < 0;

    // A script call or condition leaf whose FullName and Parameters are unchanged keeps
    // its original JSON (issue 115): a script's own Conditional, plus Flags, UnrealCall,
    // FunctionHash and ParameterHash, none of which the model carries. Matching is
    // one-to-one in order, so identical calls with different Conditionals stay distinct.
    private static JsonNode? TakeMatch(List<JsonNode> unused, string fullName, IReadOnlyList<string> parameters)
    {
        var match = unused.FirstOrDefault(e =>
            e["Data"]?["FullName"]?.GetValue<string>() == fullName &&
            (e["Data"]?["Parameters"]?.AsArray().Select(p => p?.GetValue<string>()) ?? [])
                .SequenceEqual(parameters));
        if (match is not null) unused.Remove(match);
        return match?.DeepClone();
    }

    private static IEnumerable<JsonNode> ConditionLeaves(JsonNode? components) =>
        components is JsonArray arr
            ? arr.OfType<JsonNode>().SelectMany(c =>
                c["Data"] is not null ? [c] : ConditionLeaves(c["Components"]))
            : [];

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
                    cloned["Conditionals"] = BuildConditionJson(link.Conditions, orig["Conditionals"]);
                arr.Add(cloned);
            }
            else
            {
                arr.Add(BuildNewLink(link));
            }
        }
        return arr;
    }

    // null (the snapshot value was "" or a name neither game uses) leaves the property
    // out, so the game's DialogueNode constructor default applies (issue 114).
    private static void SetEnumOrRemove(JsonNode node, string name, int? value)
    {
        if (value is { } v) node[name] = v;
        else                node.AsObject().Remove(name);
    }

    private static JsonNode BuildNewNode(NodeEditSnapshot snap)
    {
        var node = BuildNewNodeBase(snap);
        SetEnumOrRemove(node, "DisplayType", Poe2EnumMaps.DisplayTypeValue(snap.DisplayType));
        SetEnumOrRemove(node, "Persistence", Poe2EnumMaps.PersistenceValue(snap.Persistence));
        // No original JSON to merge with — every link on a brand-new node is new.
        node["Links"]           = BuildLinks(snap.Links, null);
        node["Conditionals"]    = BuildConditionJson(snap.Conditions);
        node["OnEnterScripts"]  = BuildScriptListJson(snap.Scripts, ScriptCategory.Enter);
        node["OnExitScripts"]   = BuildScriptListJson(snap.Scripts, ScriptCategory.Exit);
        node["OnUpdateScripts"] = BuildScriptListJson(snap.Scripts, ScriptCategory.Update);
        return node;
    }

    private static JsonArray BuildScriptListJson(
        IReadOnlyList<ScriptCall> scripts,
        ScriptCategory category,
        JsonArray? original = null)
    {
        var unused = original?.OfType<JsonNode>().ToList() ?? [];
        var arr    = new JsonArray();
        foreach (var s in scripts.Where(sc => sc.Category == category))
        {
            if (TakeMatch(unused, s.FullName, s.Parameters) is { } kept) { arr.Add(kept); continue; }

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

    private static JsonNode BuildConditionJson(IReadOnlyList<ConditionNode> conditions, JsonNode? original = null)
    {
        var unusedLeaves = ConditionLeaves(original?["Components"]).ToList();
        var components   = new JsonArray();
        foreach (var c in conditions)
            components.Add(BuildConditionComponentJson(c, unusedLeaves));
        // The root Operator is not modelled (the game combines components by their own
        // Operator), so keep whatever the file had.
        return new JsonObject
        {
            ["Operator"]   = original?["Operator"]?.DeepClone() ?? JsonValue.Create(0),
            ["Components"] = components,
        };
    }

    private static JsonNode BuildConditionComponentJson(ConditionNode node, List<JsonNode> unusedLeaves)
    {
        if (node is ConditionLeaf leaf)
        {
            if (TakeMatch(unusedLeaves, leaf.FullName, leaf.Parameters) is { } kept)
            {
                kept["Not"]      = leaf.Not;
                kept["Operator"] = leaf.Operator == "Or" ? 1 : 0;
                return kept;
            }

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
        foreach (var c in branch.Components) childComps.Add(BuildConditionComponentJson(c, unusedLeaves));
        // OEIFormats' ConditionalExpression has only Operator and Components (Not lives on
        // ConditionalCall), so no "Not" is written; property order matches shipped files.
        return new JsonObject
        {
            ["$type"]      = "OEIFormats.FlowCharts.ConditionalExpression, OEIFormats",
            ["Operator"]   = branch.Operator == "Or" ? 1 : 0,
            ["Components"] = childComps,
        };
    }

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
