using DialogEditor.Core.GameData;
using DialogEditor.Patch;

namespace DialogEditor.Tests.GameData;

public class Poe2GameDataProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly Poe2GameDataProvider _provider;

    public Poe2GameDataProviderTests()
    {
        Directory.CreateDirectory(_root);
        _provider = new Poe2GameDataProvider(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    // ── Fixtures ──────────────────────────────────────────────────────────

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
              "IsQuestionNode": false, "DisplayType": 1, "Persistence": 1,
              "NodeID": 1, "ContainerNodeID": -1,
              "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "NotSkippable": false, "HideSpeaker": false, "IsTempText": false,
              "PlayVOAs3DSound": false, "PlayType": 0, "NoPlayRandomWeight": 0,
              "VOPositioning": 0, "EmotionType": "", "EmotionStrength": 0.5,
              "PersistEmotion": true, "EmotionDelay": 0.0, "ExternalVO": "", "HasVO": false
            }
          ]
        }]}
        """;

    private const string SpeakersJson = """
        {"GameDataObjects":[
          {"ID":"bbbbbbbb-0000-0000-0000-000000000001","DebugName":"SPK_NPC_TestSpeaker"}
        ]}
        """;

    // ── Helpers ───────────────────────────────────────────────────────────

    private string ConvDir => Path.Combine(_root, "PillarsOfEternityII_Data", "exported", "design", "conversations");

    private string WriteBundle(string name, string json)
    {
        Directory.CreateDirectory(ConvDir);
        var path = Path.Combine(ConvDir, name + ".conversationbundle");
        File.WriteAllText(path, json);
        return path;
    }

    // ── Tests ─────────────────────────────────────────────────────────────

    [Fact]
    public void EnumerateConversations_ReturnsConversationBundleFiles()
    {
        WriteBundle("conv1", TwoNodeJson);
        WriteBundle("conv2", TwoNodeJson);

        var files = _provider.EnumerateConversations();

        Assert.Equal(2, files.Count);
    }

    [Fact]
    public void EnumerateConversations_IgnoresNonBundleFiles()
    {
        WriteBundle("valid", TwoNodeJson);
        Directory.CreateDirectory(ConvDir);
        File.WriteAllText(Path.Combine(ConvDir, "notes.txt"), "ignore me");

        var files = _provider.EnumerateConversations();

        Assert.Single(files);
        Assert.Equal("valid", files[0].Name);
    }

    [Fact]
    public void LoadConversation_ReturnsConversationWithNodes()
    {
        var path = WriteBundle("test", TwoNodeJson);
        var file = new ConversationFile("test", "", path, "");

        var conversation = _provider.LoadConversation(file);

        Assert.Equal(2, conversation.Nodes.Count);
    }

    [Fact]
    public void SaveConversation_WritesFileToExpectedPath()
    {
        var path = WriteBundle("test", TwoNodeJson);
        var file = new ConversationFile("test", "", path, "");
        var conversation = _provider.LoadConversation(file);
        var snapshot = ConversationSnapshotBuilder.Build(conversation);

        _provider.SaveConversation(file, snapshot);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void SaveConversation_RoundTrip_PreservesNodeCount()
    {
        var path = WriteBundle("test", TwoNodeJson);
        var file = new ConversationFile("test", "", path, "");
        var original = _provider.LoadConversation(file);
        var snapshot = ConversationSnapshotBuilder.Build(original);

        _provider.SaveConversation(file, snapshot);
        var reloaded = _provider.LoadConversation(file);

        Assert.Equal(original.Nodes.Count, reloaded.Nodes.Count);
    }

    [Fact]
    public void LoadSpeakerNames_WithSpeakersFile_ReturnsMappings()
    {
        var gameDataDir = Path.Combine(_root, "PillarsOfEternityII_Data", "exported", "design", "gamedata");
        Directory.CreateDirectory(gameDataDir);
        File.WriteAllText(Path.Combine(gameDataDir, "speakers.gamedatabundle"), SpeakersJson);

        var names = _provider.LoadSpeakerNames();

        Assert.Contains("bbbbbbbb-0000-0000-0000-000000000001", names.Keys,
            StringComparer.OrdinalIgnoreCase);
        Assert.Equal("TestSpeaker", names["bbbbbbbb-0000-0000-0000-000000000001"]);
    }

    [Fact]
    public void LoadSpeakerNames_WithoutSpeakersFile_ReturnsEmpty()
    {
        var names = _provider.LoadSpeakerNames();
        Assert.Empty(names);
    }

    [Fact]
    public void InitializeConversationFile_CreatesFileOnDisk()
    {
        var file = _provider.BuildNewConversationFile("newconv");

        _provider.InitializeConversationFile(file);

        Assert.True(File.Exists(file.ConversationPath));
        Assert.NotEmpty(File.ReadAllText(file.ConversationPath));
    }

    [Fact]
    public void GetStringTablePath_WithLanguage_ReturnsCorrectPath()
    {
        WriteBundle("conv1", TwoNodeJson);
        var file   = _provider.EnumerateConversations().First();
        var enPath = _provider.GetStringTablePath(file);
        var frPath = _provider.GetStringTablePath(file, "fr");
        Assert.Contains(Path.Combine("localized", "fr"), frPath);
        Assert.Contains(Path.Combine("localized", "en"), enPath);
        Assert.Equal(Path.GetFileName(enPath), Path.GetFileName(frPath));
    }

    [Fact]
    public void SaveConversation_DoesNotWriteStringtable()
    {
        var path     = WriteBundle("test", TwoNodeJson);
        var file     = new ConversationFile("test", "", path, "");
        var conv     = _provider.LoadConversation(file);
        var snap     = ConversationSnapshotBuilder.Build(conv);
        var stPath   = _provider.GetStringTablePath(file);
        var stBefore = File.Exists(stPath) ? File.ReadAllText(stPath) : null;
        _provider.SaveConversation(file, snap);
        var stAfter  = File.Exists(stPath) ? File.ReadAllText(stPath) : null;
        Assert.Equal(stBefore, stAfter);
    }

    [Fact]
    public void LoadGameDataNames_IncludesConversationKind_WithFilenameAsName()
    {
        WriteBundle("testconv", TwoNodeJson);

        var names = _provider.LoadGameDataNames();

        Assert.True(names.ContainsKey("Conversation"));
        var conv = names["Conversation"];
        Assert.Single(conv);
        Assert.Equal("testconv", conv[0].Name);
        Assert.Equal("daa2b624-875f-49bb-a041-ded56da97bea", conv[0].Id);
    }

    [Fact]
    public void LoadGameDataNames_Conversation_SkipsBundleWithNoId()
    {
        WriteBundle("noid", """{"Conversations":[{}]}""");
        WriteBundle("valid", TwoNodeJson);

        var names = _provider.LoadGameDataNames();

        var conv = names["Conversation"];
        Assert.Single(conv);
        Assert.Equal("valid", conv[0].Name);
    }
}
