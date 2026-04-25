using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class Poe2ConversationParserTests
{
    private const string TwoNodeJson = """
        {"Conversations": [{
          "ID": "daa2b624-875f-49bb-a041-ded56da97bea",
          "Filename": "Conversations/Test/test.conversation",
          "ConversationScriptNode": {
            "NodeID": -200, "ContainerNodeID": -1, "Links": [],
            "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
            "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
            "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0, "VOPositioning": 0
          },
          "ConversationType": 0,
          "CharacterMappings": [],
          "Nodes": [
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "fbeeeff7-ec6a-4a40-a47f-1843eaffc6ae",
              "ListenerGuid": "b1a8e901-0000-0000-0000-000000000000",
              "IsQuestionNode": false,
              "DisplayType": 0,
              "Persistence": 0,
              "NodeID": 0, "ContainerNodeID": -1,
              "Links": [{
                "$type": "OEIFormats.FlowCharts.Conversations.DialogueLink, OEIFormats",
                "FromNodeID": 0, "ToNodeID": 1, "PointsToGhost": false,
                "Conditionals": {"Operator": 0, "Components": []},
                "ClassExtender": {"ExtendedProperties": []},
                "RandomWeight": 1, "PlayQuestionNodeVO": true, "QuestionNodeTextDisplay": 0
              }],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
              "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0,
              "VOPositioning": 0, "EmotionType": "", "EmotionStrength": 0.5,
              "PersistEmotion": true, "EmotionDelay": 0.0, "ExternalVO": "", "HasVO": false
            },
            {
              "$type": "OEIFormats.FlowCharts.Conversations.PlayerResponseNode, OEIFormats",
              "SpeakerGuid": "b1a8e901-0000-0000-0000-000000000000",
              "ListenerGuid": "fbeeeff7-ec6a-4a40-a47f-1843eaffc6ae",
              "IsQuestionNode": false,
              "DisplayType": 1,
              "Persistence": 1,
              "NodeID": 1, "ContainerNodeID": -1,
              "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {
                "Operator": 0,
                "Components": [{
                  "$type": "OEIFormats.FlowCharts.ConditionalCall, OEIFormats",
                  "Data": {
                    "FullName": "Boolean IsGlobalValue(String, Operator, Int32)",
                    "Parameters": ["some_flag", "EqualTo", "1"]
                  },
                  "Not": false,
                  "Operator": 0
                }]
              },
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
              "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0,
              "VOPositioning": 0, "EmotionType": "", "EmotionStrength": 0.5,
              "PersistEmotion": true, "EmotionDelay": 0.0, "ExternalVO": "", "HasVO": false
            }
          ]
        }]}
        """;

    [Fact]
    public void Parse_SkipsScriptNode_ReturnsTwoRealNodes()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.Equal(2, nodes.Count);
    }

    [Fact]
    public void Parse_Node0_IsNotPlayerChoice()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.False(nodes[0].IsPlayerChoice);
    }

    [Fact]
    public void Parse_Node1_IsPlayerChoice()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.True(nodes[1].IsPlayerChoice);
    }

    [Fact]
    public void Parse_Node1_DisplayTypeAndPersistenceMappedToStrings()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.Equal("Bark", nodes[1].DisplayType);
        Assert.Equal("OnceEver", nodes[1].Persistence);
    }

    [Fact]
    public void Parse_Node0_HasOneLink()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.Single(nodes[0].Links);
        Assert.Equal(1, nodes[0].Links[0].ToNodeId);
    }

    [Fact]
    public void Parse_Node1_HasCondition()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.True(nodes[1].HasConditions);
        Assert.Equal("IsGlobalValue(some_flag, EqualTo, 1)", nodes[1].ConditionStrings[0]);
    }

    private const string TalkNodeQuestionJson = """
        {"Conversations": [{"ID": "00000000-0000-0000-0000-000000000000",
          "ConversationScriptNode": {"NodeID": -200, "Links": [], "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
            "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
            "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0, "VOPositioning": 0},
          "ConversationType": 0, "CharacterMappings": [],
          "Nodes": [{
            "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
            "SpeakerGuid": "00000000-0000-0000-0000-000000000000",
            "ListenerGuid": "00000000-0000-0000-0000-000000000000",
            "IsQuestionNode": true,
            "DisplayType": 0, "Persistence": 0,
            "NodeID": 0, "ContainerNodeID": -1, "Links": [],
            "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
            "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0, "VOPositioning": 0
          }]
        }]}
        """;

    [Fact]
    public void Parse_TalkNode_WithIsQuestionNodeTrue_IsNotPlayerChoice()
    {
        var nodes = Poe2ConversationParser.ParseJson(TalkNodeQuestionJson);
        Assert.False(nodes[0].IsPlayerChoice);
    }

    // SpeakerGuid = 6a99a109-... (SPK_Narrator in speakers.gamedatabundle)
    private const string NarratorTalkNodeJson = """
        {"Conversations": [{"ID": "00000000-0000-0000-0000-000000000000",
          "ConversationScriptNode": {"NodeID": -200, "Links": [], "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
            "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
            "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0, "VOPositioning": 0},
          "ConversationType": 0, "CharacterMappings": [],
          "Nodes": [{
            "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
            "SpeakerGuid": "6a99a109-0000-0000-0000-000000000000",
            "ListenerGuid": "b1a8e901-0000-0000-0000-000000000000",
            "IsQuestionNode": false,
            "DisplayType": 0, "Persistence": 0,
            "NodeID": 0, "ContainerNodeID": -1, "Links": [],
            "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
            "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0, "VOPositioning": 0
          }]
        }]}
        """;

    private const string ScriptNodePoe2Json = """
        {"Conversations": [{"ID": "00000000-0000-0000-0000-000000000000",
          "ConversationScriptNode": {"NodeID": -200, "Links": [], "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
            "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
            "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0, "VOPositioning": 0},
          "ConversationType": 0, "CharacterMappings": [],
          "Nodes": [{
            "$type": "OEIFormats.FlowCharts.Conversations.ScriptNode, OEIFormats",
            "NodeID": 0, "ContainerNodeID": -1, "Links": [],
            "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
            "NotSkippable": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0
          }]
        }]}
        """;

    [Fact]
    public void Parse_Poe2PlayerResponseNode_HasSpeakerCategoryPlayer()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.Equal(SpeakerCategory.Player, nodes[1].SpeakerCategory);
    }

    [Fact]
    public void Parse_Poe2NpcTalkNode_HasSpeakerCategoryNpc()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.Equal(SpeakerCategory.Npc, nodes[0].SpeakerCategory);
    }

    [Fact]
    public void Parse_Poe2NullGuidTalkNode_HasSpeakerCategoryNpc()
    {
        // 00000000-... in PoE2 means "unset speaker", not the narrator
        var nodes = Poe2ConversationParser.ParseJson(TalkNodeQuestionJson);
        Assert.Equal(SpeakerCategory.Npc, nodes[0].SpeakerCategory);
    }

    [Fact]
    public void Parse_Poe2NarratorGuidTalkNode_HasSpeakerCategoryNarrator()
    {
        // 6a99a109-... is the PoE2 narrator (SPK_Narrator in speakers.gamedatabundle)
        var nodes = Poe2ConversationParser.ParseJson(NarratorTalkNodeJson);
        Assert.Equal(SpeakerCategory.Narrator, nodes[0].SpeakerCategory);
    }

    [Fact]
    public void Parse_Poe2ScriptNode_HasSpeakerCategoryScript()
    {
        var nodes = Poe2ConversationParser.ParseJson(ScriptNodePoe2Json);
        Assert.Equal(SpeakerCategory.Script, nodes[0].SpeakerCategory);
    }

    private const string ScriptedTalkNodeJson = """
        {"Conversations": [{"ID": "00000000-0000-0000-0000-000000000000",
          "ConversationScriptNode": {"NodeID": -200, "Links": [], "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
            "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
            "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0, "VOPositioning": 0},
          "ConversationType": 0, "CharacterMappings": [],
          "Nodes": [{
            "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
            "SpeakerGuid": "00000000-0000-0000-0000-000000000000",
            "ListenerGuid": "00000000-0000-0000-0000-000000000000",
            "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
            "NodeID": 0, "ContainerNodeID": -1, "Links": [],
            "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [{
              "Data": {
                "FullName": "Void ActivateObject(Guid, Boolean)",
                "Parameters": ["546e5d97-760e-4d7d-b03a-cc01c0f3ce43", "False"]
              },
              "Conditional": {"Operator": 0, "Components": []}
            }],
            "OnExitScripts": [{
              "Data": {
                "FullName": "Void DeactivateObject(Guid)",
                "Parameters": ["546e5d97-760e-4d7d-b03a-cc01c0f3ce43"]
              },
              "Conditional": {"Operator": 0, "Components": []}
            }],
            "OnUpdateScripts": [],
            "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0, "VOPositioning": 0
          }]
        }]}
        """;

    [Fact]
    public void Parse_Poe2Node_WithOnEnterAndExitScripts_HasTwoScriptStrings()
    {
        var nodes = Poe2ConversationParser.ParseJson(ScriptedTalkNodeJson);
        Assert.Equal(2, nodes[0].Scripts.Count);
        Assert.Contains("[Enter]", nodes[0].Scripts[0]);
        Assert.Contains("ActivateObject", nodes[0].Scripts[0]);
        Assert.Contains("[Exit]", nodes[0].Scripts[1]);
        Assert.Contains("DeactivateObject", nodes[0].Scripts[1]);
    }

    private const string VoiceFieldsJson = """
        {"Conversations": [{"ID": "00000000-0000-0000-0000-000000000000",
          "ConversationScriptNode": {"NodeID": -200, "Links": [], "ClassExtender": {"ExtendedProperties": []},
            "Conditionals": {"Operator": 0, "Components": []},
            "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
            "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
            "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
            "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0, "VOPositioning": 0},
          "ConversationType": 0, "CharacterMappings": [],
          "Nodes": [
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "fbeeeff7-ec6a-4a40-a47f-1843eaffc6ae",
              "ListenerGuid": "b1a8e901-0000-0000-0000-000000000000",
              "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
              "NodeID": 0, "ContainerNodeID": -1,
              "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "NotSkippable": false, "HideSpeaker": true, "IsTempText": false,
              "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0,
              "VOPositioning": 0, "EmotionType": "", "EmotionStrength": 0.5,
              "PersistEmotion": true, "EmotionDelay": 0.0,
              "ExternalVO": "npc_aloth/prologue_awakens_0001", "HasVO": true
            }
          ]
        }]}
        """;

    [Fact]
    public void Parse_Poe2Node_WithExternalVO_HasExternalVO()
    {
        var nodes = Poe2ConversationParser.ParseJson(VoiceFieldsJson);
        Assert.Equal("npc_aloth/prologue_awakens_0001", nodes[0].ExternalVO);
    }

    [Fact]
    public void Parse_Poe2Node_WithHasVOTrue_HasVOIsTrue()
    {
        var nodes = Poe2ConversationParser.ParseJson(VoiceFieldsJson);
        Assert.True(nodes[0].HasVO);
    }

    [Fact]
    public void Parse_Poe2Node_WithHideSpeakerTrue_HideSpeakerIsTrue()
    {
        var nodes = Poe2ConversationParser.ParseJson(VoiceFieldsJson);
        Assert.True(nodes[0].HideSpeaker);
    }

    [Fact]
    public void Parse_Poe2Link_HasRandomWeight()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.Equal(1f, nodes[0].Links[0].RandomWeight);
    }

    [Fact]
    public void Parse_Poe2FlatCondition_ConditionExpressionMatchesLeaf()
    {
        var nodes = Poe2ConversationParser.ParseJson(TwoNodeJson);
        Assert.Equal("IsGlobalValue(some_flag, EqualTo, 1)", nodes[1].ConditionExpression);
    }
}
