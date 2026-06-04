using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class DialogProjectLineMapTests
{
    // Builds JSON from explicit lines so each line number is unambiguous (1-based).
    private static string Json(params string[] lines) => string.Join("\n", lines);

    private static NodeEditSnapshot Node(int id) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false, [], [], []);

    [Fact]
    public void AddedNode_MapsToObjectLineRange()
    {
        var json = Json(
            "{",                                    // 1
            "  \"Patches\": {",                     // 2
            "    \"greeting\": {",                  // 3
            "      \"AddedNodes\": [",              // 4
            "        {",                            // 5
            "          \"NodeId\": 1,",             // 6
            "          \"IsPlayerChoice\": false",  // 7
            "        }",                            // 8
            "      ]",                              // 9
            "    }",                                // 10
            "  }",                                  // 11
            "}");                                   // 12

        var map = DialogProjectLineMap.Build(json);

        Assert.Equal(new[] { (5, 8) }, map[("greeting", 1)]);
    }

    [Fact]
    public void Node_AcrossStructureTextAndComment_HasAllRanges()
    {
        var json = Json(
            "{",                                       // 1
            "  \"Name\": \"M\",",                      // 2
            "  \"SchemaVersion\": 2,",                 // 3
            "  \"Patches\": {",                        // 4
            "    \"greeting\": {",                     // 5
            "      \"ConversationName\": \"greeting\",",// 6
            "      \"SchemaVersion\": 2,",             // 7
            "      \"AddedNodes\": [",                 // 8
            "        {",                               // 9
            "          \"NodeId\": 1,",                // 10
            "          \"IsPlayerChoice\": false",     // 11
            "        }",                               // 12
            "      ],",                                // 13
            "      \"DeletedNodeIds\": [],",           // 14
            "      \"ModifiedNodes\": [],",            // 15
            "      \"Translations\": {",               // 16
            "        \"en\": [",                       // 17
            "          {",                             // 18
            "            \"NodeId\": 1,",              // 19
            "            \"DefaultText\": \"Hi\",",    // 20
            "            \"FemaleText\": \"\"",        // 21
            "          }",                             // 22
            "        ]",                               // 23
            "      },",                                // 24
            "      \"NodeComments\": {",               // 25
            "        \"1\": \"note\"",                 // 26
            "      }",                                 // 27
            "    }",                                   // 28
            "  },",                                    // 29
            "  \"Layouts\": null,",                    // 30
            "  \"NewConversations\": null",            // 31
            "}");                                      // 32

        var map = DialogProjectLineMap.Build(json);

        Assert.Equal(new[] { (9, 12), (18, 22), (26, 26) }, map[("greeting", 1)]);
    }

    [Fact]
    public void Translations_MultipleLanguages_EachContributeARange()
    {
        var json = Json(
            "{",                                  // 1
            "  \"Patches\": {",                   // 2
            "    \"c\": {",                       // 3
            "      \"Translations\": {",          // 4
            "        \"en\": [",                  // 5
            "          {",                        // 6
            "            \"NodeId\": 7,",         // 7
            "            \"DefaultText\": \"Hi\"",// 8
            "          }",                        // 9
            "        ],",                         // 10
            "        \"fr\": [",                  // 11
            "          {",                        // 12
            "            \"NodeId\": 7,",         // 13
            "            \"DefaultText\": \"Bonjour\"",// 14
            "          }",                        // 15
            "        ]",                          // 16
            "      }",                            // 17
            "    }",                              // 18
            "  }",                                // 19
            "}");                                 // 20

        var map = DialogProjectLineMap.Build(json);

        Assert.Equal(new[] { (6, 9), (12, 15) }, map[("c", 7)]);
    }

    [Fact]
    public void ModifiedNode_MapsToObjectLineRange()
    {
        var json = Json(
            "{",                            // 1
            "  \"Patches\": {",             // 2
            "    \"c\": {",                 // 3
            "      \"ModifiedNodes\": [",   // 4
            "        {",                    // 5
            "          \"NodeId\": 3",      // 6
            "        }",                    // 7
            "      ]",                      // 8
            "    }",                        // 9
            "  }",                          // 10
            "}");                           // 11

        var map = DialogProjectLineMap.Build(json);

        Assert.Equal(new[] { (5, 7) }, map[("c", 3)]);
    }

    [Fact]
    public void NestedLinkObjects_DoNotLeakAsBogusNodes()
    {
        // A link's ToNodeId must not be mistaken for a node; the node range still
        // spans the whole node object (links included).
        var json = Json(
            "{",                                  // 1
            "  \"Patches\": {",                   // 2
            "    \"c\": {",                       // 3
            "      \"AddedNodes\": [",            // 4
            "        {",                          // 5
            "          \"NodeId\": 5,",           // 6
            "          \"Links\": [",             // 7
            "            {",                      // 8
            "              \"FromNodeId\": 5,",   // 9
            "              \"ToNodeId\": 9",      // 10
            "            }",                      // 11
            "          ]",                        // 12
            "        }",                          // 13
            "      ]",                            // 14
            "    }",                              // 15
            "  }",                                // 16
            "}");                                 // 17

        var map = DialogProjectLineMap.Build(json);

        Assert.Equal(new[] { (5, 13) }, map[("c", 5)]);
        Assert.False(map.ContainsKey(("c", 9)));
    }

    [Fact]
    public void AlignsWithRealSerializerOutput()
    {
        // Guards against drift between the mapper and actual DialogProjectSerializer
        // formatting (indentation, property order). Asserts against independently
        // located lines rather than hard-coded numbers.
        var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
            [Node(7)], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { ["en"] = [new NodeTranslation(7, "Hi", "")] },
            NodeComments = new Dictionary<int, string> { [7] = "note" },
        };
        var project = new DialogProject("M", ConversationPatch.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch> { ["greeting"] = patch });
        var json = DialogProjectSerializer.Serialize(project);

        var map = DialogProjectLineMap.Build(json);
        var lines = json.Replace("\r\n", "\n").Split('\n');

        Assert.True(map.ContainsKey(("greeting", 7)));
        var ranges = map[("greeting", 7)];

        // The structural "NodeId": 7 line is covered by some range.
        var nodeIdLine = Array.FindIndex(lines, l => l.Contains("\"NodeId\": 7")) + 1;
        Assert.Contains(ranges, r => r.Start <= nodeIdLine && nodeIdLine <= r.End);

        // The NodeComments entry line ("7": "note") is covered too.
        var commentLine = Array.FindIndex(lines, l => l.Contains("\"7\": \"note\"")) + 1;
        Assert.Contains(ranges, r => r.Start <= commentLine && commentLine <= r.End);
    }
}
