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

    private const string PlayerTalkNodeJson = """
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
    public void Parse_Poe2NarratorTalkNode_HasSpeakerCategoryNarrator()
    {
        var nodes = Poe2ConversationParser.ParseJson(TalkNodeQuestionJson);
        Assert.Equal(SpeakerCategory.Narrator, nodes[0].SpeakerCategory);
    }

    [Fact]
    public void Parse_Poe2CompanionSlotGuidTalkNode_HasSpeakerCategoryNpc()
    {
        // 6a99a109-... is a generic runtime companion slot, not the player
        var nodes = Poe2ConversationParser.ParseJson(PlayerTalkNodeJson);
        Assert.Equal(SpeakerCategory.Npc, nodes[0].SpeakerCategory);
    }

    [Fact]
    public void Parse_Poe2ScriptNode_HasSpeakerCategoryScript()
    {
        var nodes = Poe2ConversationParser.ParseJson(ScriptNodePoe2Json);
        Assert.Equal(SpeakerCategory.Script, nodes[0].SpeakerCategory);
    }
}
