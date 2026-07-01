using System.Text.Json.Nodes;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Tests.Serialization;

public class Poe2ConversationSerializerTests
{
    private const string TwoNodeJson = """
        {"Conversations": [{
          "Nodes": [
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "aaaa-0000", "ListenerGuid": "bbbb-0000",
              "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
              "NodeID": 0, "ContainerNodeID": -1,
              "Links": [{
                "$type": "OEIFormats.FlowCharts.Conversations.DialogueLink, OEIFormats",
                "FromNodeID": 0, "ToNodeID": 1, "PointsToGhost": false,
                "Conditionals": {"Operator": 0, "Components": []},
                "ClassExtender": {"ExtendedProperties": []},
                "RandomWeight": 1, "PlayQuestionNodeVO": true, "QuestionNodeTextDisplay": 0
              }],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": [{"Data":{"FullName":"SomeCondition","Parameters":[]}}]},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "HideSpeaker": false, "HasVO": false, "ExternalVO": "",
              "IsTempText": false, "PlayVOAs3DSound": false, "PlayType": 0,
              "NoPlayRandomWeight": 0, "VOPositioning": 0, "NotSkippable": false
            },
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "cccc-0000", "ListenerGuid": "dddd-0000",
              "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
              "NodeID": 1, "ContainerNodeID": -1, "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "HideSpeaker": false, "HasVO": false, "ExternalVO": "",
              "IsTempText": false, "PlayVOAs3DSound": false, "PlayType": 0,
              "NoPlayRandomWeight": 0, "VOPositioning": 0, "NotSkippable": false
            }
          ]
        }]}
        """;

    private static NodeEditSnapshot Node(int id, string speakerGuid = "aaaa-0000",
        IReadOnlyList<LinkEditSnapshot>? links = null) =>
        new(id, false, SpeakerCategory.Npc, speakerGuid, "bbbb-0000",
            "text", "", "Conversation", "None", "", "", "", false, false,
            links ?? [], [], []);

    [Fact]
    public void Serialize_UpdatesSpeakerGuid()
    {
        var snapshot = new ConversationEditSnapshot([Node(0, speakerGuid: "new-guid"), Node(1)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var nodes = Poe2ConversationParser.ParseJson(result);
        Assert.Equal("new-guid", nodes[0].SpeakerGuid);
    }

    [Fact]
    public void Serialize_RoundTripsConditions()
    {
        // Parse → snapshot with parsed conditions → serialize → parse again
        var parsedNodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        var snap0 = Node(0) with { Conditions = parsedNodes[0].Conditions };
        var snapshot = new ConversationEditSnapshot([snap0, Node(1)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var root = JsonNode.Parse(result)!;
        var condComponents = root["Conversations"]![0]!["Nodes"]![0]!
            ["Conditionals"]!["Components"]!.AsArray();
        Assert.NotEmpty(condComponents);
    }

    [Fact]
    public void Serialize_DeletesRemovedNode()
    {
        var snapshot = new ConversationEditSnapshot([Node(0)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var nodes = Poe2ConversationParser.ParseJson(result);
        Assert.DoesNotContain(nodes, n => n.NodeId == 1);
    }

    [Fact]
    public void Serialize_AddsNewNode()
    {
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1), Node(99)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var nodes = Poe2ConversationParser.ParseJson(result);
        Assert.Contains(nodes, n => n.NodeId == 99);
    }

    [Fact]
    public void Serialize_AddsNewNode_KeepsItsOutgoingLinks()
    {
        // Regression (B-005): a node added by the editor (absent from the original
        // JSON) must be written with its outgoing links, or it is a dead end in-game.
        var links    = new[] { new LinkEditSnapshot(99, 1, 1f, "", false) };
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1), Node(99, links: links)]);

        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);

        var nodes = Poe2ConversationParser.ParseJson(result);
        var added = nodes.Single(n => n.NodeId == 99);
        var link  = Assert.Single(added.Links);
        Assert.Equal(1, link.ToNodeId);
    }

    [Fact]
    public void Serialize_RebuildLinks()
    {
        var links = new[] { new LinkEditSnapshot(0, 1, 1f, "ShowOnce", false) };
        var snapshot = new ConversationEditSnapshot([Node(0, links: links), Node(1)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var nodes = Poe2ConversationParser.ParseJson(result);
        Assert.Single(nodes[0].Links);
        Assert.Equal(1, nodes[0].Links[0].ToNodeId);
    }

    private static NodeEditSnapshot ScriptNode(int id) =>
        new(id, false, SpeakerCategory.Script, "aaaa-0000", "bbbb-0000",
            "text", "", "Conversation", "None", "", "", "", false, false,
            [], [], []);

    private const string ScriptNodeType = "OEIFormats.FlowCharts.Conversations.ScriptNode, OEIFormats";

    [Fact]
    public void Serialize_ScriptNode_EmitsScriptNodeType()
    {
        var snapshot = new ConversationEditSnapshot([ScriptNode(0), Node(1)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var root = JsonNode.Parse(result)!;
        var type = root["Conversations"]![0]!["Nodes"]![0]!["$type"]!.GetValue<string>();
        Assert.Equal(ScriptNodeType, type);
    }

    [Fact]
    public void Serialize_NewScriptNode_EmitsScriptNodeType()
    {
        var snapshot = new ConversationEditSnapshot([Node(0), Node(1), ScriptNode(99)]);
        var result = Poe2ConversationSerializer.Serialize(TwoNodeJson, snapshot);
        var root = JsonNode.Parse(result)!;
        var nodes = root["Conversations"]![0]!["Nodes"]!.AsArray();
        var node99 = nodes.First(n => n!["NodeID"]!.GetValue<int>() == 99)!;
        Assert.Equal(ScriptNodeType, node99["$type"]!.GetValue<string>());
    }
}
