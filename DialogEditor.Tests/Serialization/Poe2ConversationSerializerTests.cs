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

    // ── #113: node types the editor does not model survive a save ────────────────
    // The parser files BankNode and TriggerConversationNode under SpeakerCategory.Script.
    // Deriving $type from the category alone rewrote them as ScriptNode, and the game
    // then discarded BankNodePlayType / ChildNodeIDs / ConversationGuid / StartNodeID.

    private const string BankNodeType    = "OEIFormats.FlowCharts.BankNode, OEIFormats";
    private const string TriggerNodeType = "OEIFormats.FlowCharts.Conversations.TriggerConversationNode, OEIFormats";

    // Shapes taken from shipped Deadfire conversations.
    private const string UnmodelledTypesJson = """
        {"Conversations": [{
          "Nodes": [
            {
              "$type": "OEIFormats.FlowCharts.BankNode, OEIFormats",
              "BankNodePlayType": 2, "Persistence": 0, "ChildNodeIDs": [1],
              "NodeID": 0, "ContainerNodeID": -1, "Links": [],
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": []
            },
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TriggerConversationNode, OEIFormats",
              "ConversationGuid": "d6231ed2-cb9a-4d6f-ac71-37142fa6a8e3", "StartNodeID": 7,
              "DisplayType": 1, "Persistence": 1,
              "NodeID": 1, "ContainerNodeID": -1, "Links": [],
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": []
            }
          ]
        }]}
        """;

    private static NodeEditSnapshot ScriptCategoryNode(int id) =>
        Node(id) with { SpeakerCategory = SpeakerCategory.Script };

    private static JsonNode SavedNode(string json, ConversationEditSnapshot snapshot, int id) =>
        JsonNode.Parse(Poe2ConversationSerializer.Serialize(json, snapshot))!
            ["Conversations"]![0]!["Nodes"]!.AsArray()
            .First(n => n!["NodeID"]!.GetValue<int>() == id)!;

    [Fact]
    public void Serialize_BankNode_KeepsTypeAndBankData()
    {
        var snapshot = new ConversationEditSnapshot([ScriptCategoryNode(0), ScriptCategoryNode(1)]);
        var node0    = SavedNode(UnmodelledTypesJson, snapshot, 0);

        Assert.Equal(BankNodeType, node0["$type"]!.GetValue<string>());
        Assert.Equal(2,            node0["BankNodePlayType"]!.GetValue<int>());
        Assert.Equal([1],          node0["ChildNodeIDs"]!.AsArray().Select(n => n!.GetValue<int>()));
    }

    [Fact]
    public void Serialize_TriggerConversationNode_KeepsTypeAndTarget()
    {
        var snapshot = new ConversationEditSnapshot([ScriptCategoryNode(0), ScriptCategoryNode(1)]);
        var node1    = SavedNode(UnmodelledTypesJson, snapshot, 1);

        Assert.Equal(TriggerNodeType, node1["$type"]!.GetValue<string>());
        Assert.Equal("d6231ed2-cb9a-4d6f-ac71-37142fa6a8e3", node1["ConversationGuid"]!.GetValue<string>());
        Assert.Equal(7, node1["StartNodeID"]!.GetValue<int>());
    }

    // ── #114: enum values follow OEIFormats' declaration order ───────────────
    // DisplayType     { Hidden, Conversation, Bark, Overlay }
    // PersistenceType { None, OnceEver, OncePerConversation, MarkAsRead }
    // The old tables wrote Conversation as 0 (Hidden), Bark as 1 (Conversation), and
    // collapsed Overlay / OncePerConversation / MarkAsRead to 0.

    [Theory]
    [InlineData("Hidden",       0)]
    [InlineData("Conversation", 1)]
    [InlineData("Bark",         2)]
    [InlineData("Overlay",      3)]
    [InlineData("Unknown(9)",   9)]
    public void Serialize_DisplayType_WritesGameEnumValue(string name, int expected)
    {
        var snapshot = new ConversationEditSnapshot([Node(0) with { DisplayType = name }, Node(1)]);
        Assert.Equal(expected, SavedNode(TwoNodeJson, snapshot, 0)["DisplayType"]!.GetValue<int>());
    }

    [Theory]
    [InlineData("None",                0)]
    [InlineData("OnceEver",            1)]
    [InlineData("OncePerConversation", 2)]
    [InlineData("MarkAsRead",          3)]
    [InlineData("Unknown(9)",          9)]
    public void Serialize_Persistence_WritesGameEnumValue(string name, int expected)
    {
        var snapshot = new ConversationEditSnapshot([Node(0) with { Persistence = name }, Node(1)]);
        Assert.Equal(expected, SavedNode(TwoNodeJson, snapshot, 0)["Persistence"]!.GetValue<int>());
    }

    [Fact]
    public void Serialize_NewNode_WritesGameEnumValues()
    {
        var snapshot = new ConversationEditSnapshot(
            [Node(0), Node(1), Node(99) with { DisplayType = "Bark", Persistence = "MarkAsRead" }]);
        var node99 = SavedNode(TwoNodeJson, snapshot, 99);

        Assert.Equal(2, node99["DisplayType"]!.GetValue<int>());
        Assert.Equal(3, node99["Persistence"]!.GetValue<int>());
    }

    [Fact]
    public void Serialize_EmptyEnumValues_AreLeftOut()
    {
        // "" is what the parser reports for an absent property (e.g. a BankNode's
        // DisplayType); absent lets the game's constructor default apply.
        var snapshot = new ConversationEditSnapshot(
            [Node(0) with { DisplayType = "", Persistence = "" }, Node(1),
             Node(99) with { DisplayType = "", Persistence = "" }]);

        foreach (var id in new[] { 0, 99 })
        {
            var node = SavedNode(TwoNodeJson, snapshot, id).AsObject();
            Assert.False(node.ContainsKey("DisplayType"), $"node {id}");
            Assert.False(node.ContainsKey("Persistence"), $"node {id}");
        }
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public void Serialize_ParsedEnumValues_RoundTripUnchanged(int value)
    {
        var json = TwoNodeJson.Replace("\"DisplayType\": 0, \"Persistence\": 0,",
                                       $"\"DisplayType\": {value}, \"Persistence\": {value},");
        var parsed   = Poe2ConversationParser.ParseJson(json);
        var snapshot = new ConversationEditSnapshot(parsed.Select(n =>
            Node(n.NodeId) with { DisplayType = n.DisplayType, Persistence = n.Persistence }).ToList());
        var node0 = SavedNode(json, snapshot, 0);

        Assert.Equal(value, node0["DisplayType"]!.GetValue<int>());
        Assert.Equal(value, node0["Persistence"]!.GetValue<int>());
    }

    [Theory]
    [InlineData(SpeakerCategory.Npc,      "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats")]
    [InlineData(SpeakerCategory.Narrator, "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats")]
    [InlineData(SpeakerCategory.Player,   "OEIFormats.FlowCharts.Conversations.PlayerResponseNode, OEIFormats")]
    public void Serialize_BankNode_UserChangesCategory_TypeFollowsCategory(SpeakerCategory category, string expected)
    {
        var snapshot = new ConversationEditSnapshot(
            [ScriptCategoryNode(0) with { SpeakerCategory = category }, ScriptCategoryNode(1)]);

        Assert.Equal(expected, SavedNode(UnmodelledTypesJson, snapshot, 0)["$type"]!.GetValue<string>());
    }

    // ── #115: logic the editor does not model survives a save ────────────────────
    // Every Deadfire bundle has a conversation-level ScriptNode with NodeID -200 (at
    // varying positions in Nodes); the parser skips it, and the serializer used to
    // rebuild Nodes from the snapshot only, deleting it. Scripts and condition leaves
    // were rebuilt from FullName + Parameters, dropping each script's own Conditional
    // and the Flags / UnrealCall / FunctionHash / ParameterHash data. Shapes below are
    // taken from shipped bundles.

    private const string HiddenLogicJson = """
        {"Conversations": [{
          "Nodes": [
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "aaaa-0000", "ListenerGuid": "bbbb-0000",
              "DisplayType": 1, "Persistence": 0, "NodeID": 0, "ContainerNodeID": -1,
              "Links": [{
                "$type": "OEIFormats.FlowCharts.Conversations.DialogueLink, OEIFormats",
                "FromNodeID": 0, "ToNodeID": 1, "PointsToGhost": false,
                "Conditionals": {"Operator": 0, "Components": [{
                  "$type": "OEIFormats.FlowCharts.ConditionalCall, OEIFormats",
                  "Data": {"FullName": "Boolean IsGlobalValue(String, Operator, Int32)",
                           "Parameters": ["n_link", "EqualTo", "1"], "Flags": "", "UnrealCall": "",
                           "FunctionHash": 901380568, "ParameterHash": 111},
                  "Not": false, "Operator": 0}]},
                "ClassExtender": {"ExtendedProperties": []},
                "RandomWeight": 1, "PlayQuestionNodeVO": true, "QuestionNodeTextDisplay": 0
              }],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 1, "Components": [{
                "$type": "OEIFormats.FlowCharts.ConditionalCall, OEIFormats",
                "Data": {"FullName": "Boolean IsGlobalValue(String, Operator, Int32)",
                         "Parameters": ["n_node", "EqualTo", "2"], "Flags": "", "UnrealCall": "",
                         "FunctionHash": 901380568, "ParameterHash": 222},
                "Not": false, "Operator": 0}]},
              "OnEnterScripts": [
                {"Data": {"FullName": "Void SetEndGameSlide(String)",
                          "Parameters": ["gui\\endgameslides\\endgameslide_04.png"], "Flags": "", "UnrealCall": "",
                          "FunctionHash": -2009207772, "ParameterHash": -512340832},
                 "Conditional": {"Operator": 0, "Components": [{
                   "$type": "OEIFormats.FlowCharts.ConditionalCall, OEIFormats",
                   "Data": {"FullName": "Boolean IsGlobalValue(String, Operator, Int32)",
                            "Parameters": ["n_PX2_end_decision", "EqualTo", "2"], "Flags": "", "UnrealCall": "",
                            "FunctionHash": 901380568, "ParameterHash": 1407746067},
                   "Not": true, "Operator": 0}]}},
                {"Data": {"FullName": "Void SetEndGameSlide(String)",
                          "Parameters": ["gui\\endgameslides\\endgameslide_04.png"], "Flags": "", "UnrealCall": "",
                          "FunctionHash": -2009207772, "ParameterHash": -512340832},
                 "Conditional": {"Operator": 0, "Components": [{
                   "$type": "OEIFormats.FlowCharts.ConditionalCall, OEIFormats",
                   "Data": {"FullName": "Boolean IsGlobalValue(String, Operator, Int32)",
                            "Parameters": ["n_PX2_end_decision", "EqualTo", "3"], "Flags": "", "UnrealCall": "",
                            "FunctionHash": 901380568, "ParameterHash": 1407746066},
                   "Not": true, "Operator": 0}]}}
              ],
              "OnExitScripts": [], "OnUpdateScripts": []
            },
            {
              "$type": "OEIFormats.FlowCharts.Conversations.ScriptNode, OEIFormats",
              "RequiresValidChildNode": false, "DisplayType": 1, "Persistence": 0,
              "NodeID": -200, "ContainerNodeID": -1, "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [{"Data": {"FullName": "Void LoadAudioBank(String)",
                                           "Parameters": ["SI_EndGame_EothasChallenge"], "Flags": "", "UnrealCall": "",
                                           "FunctionHash": 1132628044, "ParameterHash": 1762804768},
                                  "Conditional": {"Operator": 0, "Components": []}}],
              "OnExitScripts": [], "OnUpdateScripts": []
            },
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "cccc-0000", "ListenerGuid": "dddd-0000",
              "DisplayType": 1, "Persistence": 0, "NodeID": 1, "ContainerNodeID": -1, "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": []
            }
          ]
        }]}
        """;

    // The snapshot an unchanged save produces: everything the parser models, as parsed.
    private static ConversationEditSnapshot UnchangedSnapshot(string json) =>
        new(Poe2ConversationParser.ParseJson(json).Select(n => new NodeEditSnapshot(
            n.NodeId, n.IsPlayerChoice, n.SpeakerCategory, n.SpeakerGuid, n.ListenerGuid,
            "", "", n.DisplayType, n.Persistence, n.ActorDirection, n.Comments, n.ExternalVO,
            n.HasVO, n.HideSpeaker,
            n.Links.Select(l => new LinkEditSnapshot(l.FromNodeId, l.ToNodeId, l.RandomWeight,
                                                     l.QuestionNodeTextDisplay, l.Conditions.Count > 0)
                                { Conditions = l.Conditions }).ToList(),
            n.Conditions, n.Scripts)).ToList());

    private static JsonArray NodesOf(string json) =>
        JsonNode.Parse(json)!["Conversations"]![0]!["Nodes"]!.AsArray();

    private static JsonNode OriginalNode(int id) =>
        NodesOf(HiddenLogicJson).First(n => n!["NodeID"]!.GetValue<int>() == id)!;

    [Fact]
    public void Serialize_ConversationScriptNode_IsKeptVerbatimInPlace()
    {
        var saved = NodesOf(Poe2ConversationSerializer.Serialize(HiddenLogicJson, UnchangedSnapshot(HiddenLogicJson)));

        Assert.Equal([0, -200, 1], saved.Select(n => n!["NodeID"]!.GetValue<int>()));
        Assert.True(JsonNode.DeepEquals(OriginalNode(-200), saved[1]));
    }

    [Fact]
    public void Serialize_ConversationScriptNode_SurvivesDeletingAndAddingNodes()
    {
        var unchanged = UnchangedSnapshot(HiddenLogicJson);
        var snapshot  = new ConversationEditSnapshot([unchanged.Nodes[0], Node(99)]);   // node 1 deleted
        var saved     = NodesOf(Poe2ConversationSerializer.Serialize(HiddenLogicJson, snapshot));

        Assert.Equal([0, -200, 99], saved.Select(n => n!["NodeID"]!.GetValue<int>()));
    }

    [Fact]
    public void Serialize_UnchangedNode_KeepsScriptConditionalsAndHashes()
    {
        var node0 = SavedNode(HiddenLogicJson, UnchangedSnapshot(HiddenLogicJson), 0);

        Assert.True(JsonNode.DeepEquals(OriginalNode(0)["OnEnterScripts"], node0["OnEnterScripts"]));
    }

    [Fact]
    public void Serialize_UnchangedNode_KeepsConditionAndLinkConditionData()
    {
        var node0 = SavedNode(HiddenLogicJson, UnchangedSnapshot(HiddenLogicJson), 0);

        // Includes the root Operator (1), which the model does not carry.
        Assert.True(JsonNode.DeepEquals(OriginalNode(0)["Conditionals"], node0["Conditionals"]));
        Assert.True(JsonNode.DeepEquals(OriginalNode(0)["Links"]![0]!["Conditionals"],
                                        node0["Links"]![0]!["Conditionals"]));
    }

    [Fact]
    public void Serialize_ConditionGroup_WritesOnlyTheGamesProperties()
    {
        // OEIFormats' ConditionalExpression has Operator and Components only; Not exists
        // on ConditionalCall. Shipped groups carry no "Not", so none may be added.
        var group = new ConditionBranch(
            [new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)", ["a", "EqualTo", "1"], false, "And")],
            false, "Or");
        var snapshot = new ConversationEditSnapshot([Node(0) with { Conditions = [group] }, Node(1)]);

        var saved = SavedNode(TwoNodeJson, snapshot, 0)["Conditionals"]!["Components"]![0]!.AsObject();

        Assert.Equal(["$type", "Operator", "Components"], saved.Select(p => p.Key));
        Assert.Equal(1, saved["Operator"]!.GetValue<int>());
    }

    [Fact]
    public void Serialize_EditedConditionFlags_KeepHashesAndApplyEdit()
    {
        var unchanged = UnchangedSnapshot(HiddenLogicJson);
        var leaf      = (ConditionLeaf)unchanged.Nodes[0].Conditions[0];
        var edited    = unchanged.Nodes[0] with { Conditions = [leaf with { Not = true }] };
        var node0     = SavedNode(HiddenLogicJson, new ConversationEditSnapshot([edited, unchanged.Nodes[1]]), 0);

        var saved = node0["Conditionals"]!["Components"]![0]!;
        Assert.True(saved["Not"]!.GetValue<bool>());
        Assert.Equal(222, saved["Data"]!["ParameterHash"]!.GetValue<int>());
    }

    [Fact]
    public void Serialize_RemovingOneOfTwoIdenticalScripts_KeepsTheOtherConditional()
    {
        // Both scripts share FullName + Parameters and differ only in their Conditional.
        // Matching is one-to-one in order, so the first original entry is reused.
        var unchanged = UnchangedSnapshot(HiddenLogicJson);
        var edited    = unchanged.Nodes[0] with { Scripts = [unchanged.Nodes[0].Scripts[0]] };
        var node0     = SavedNode(HiddenLogicJson, new ConversationEditSnapshot([edited, unchanged.Nodes[1]]), 0);

        var scripts = node0["OnEnterScripts"]!.AsArray();
        Assert.Single(scripts);
        Assert.True(JsonNode.DeepEquals(OriginalNode(0)["OnEnterScripts"]![0], scripts[0]));
    }

    [Fact]
    public void Serialize_EditedScriptParameters_WritesTheEdit()
    {
        var unchanged = UnchangedSnapshot(HiddenLogicJson);
        var script    = unchanged.Nodes[0].Scripts[0] with { Parameters = ["gui\\endgameslides\\other.png"] };
        var edited    = unchanged.Nodes[0] with { Scripts = [script] };
        var node0     = SavedNode(HiddenLogicJson, new ConversationEditSnapshot([edited, unchanged.Nodes[1]]), 0);

        var saved = Assert.Single(node0["OnEnterScripts"]!.AsArray())!;
        Assert.Equal("gui\\endgameslides\\other.png", saved["Data"]!["Parameters"]![0]!.GetValue<string>());
    }
}
