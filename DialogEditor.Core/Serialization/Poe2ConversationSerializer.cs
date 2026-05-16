using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using DialogEditor.Core.Editing;

namespace DialogEditor.Core.Serialization;

public static class Poe2ConversationSerializer
{
    private const string TalkNodeType     = "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats";
    private const string PlayerNodeType   = "OEIFormats.FlowCharts.Conversations.PlayerResponseNode, OEIFormats";
    private const string DialogueLinkType = "OEIFormats.FlowCharts.Conversations.DialogueLink, OEIFormats";

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
        node["$type"]        = snap.IsPlayerChoice ? PlayerNodeType : TalkNodeType;
        node["SpeakerGuid"]  = snap.SpeakerGuid;
        node["ListenerGuid"] = snap.ListenerGuid;
        node["DisplayType"]  = MapDisplayType(snap.DisplayType);
        node["Persistence"]  = MapPersistence(snap.Persistence);
        node["HideSpeaker"]  = snap.HideSpeaker;
        node["HasVO"]        = snap.HasVO;
        node["ExternalVO"]   = snap.ExternalVO;
        node["Links"]        = BuildLinks(snap.Links, original["Links"]?.AsArray());
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
                arr.Add(cloned);
            }
            else
            {
                arr.Add(BuildNewLink(link));
            }
        }
        return arr;
    }

    private static JsonNode BuildNewNode(NodeEditSnapshot snap) => JsonNode.Parse($$"""
        {
          "$type": "{{(snap.IsPlayerChoice ? PlayerNodeType : TalkNodeType)}}",
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

    private static JsonNode BuildNewLink(LinkEditSnapshot link) => JsonNode.Parse($$"""
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
