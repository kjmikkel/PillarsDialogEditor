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

    private string GameDataDir()
    {
        var dir = Path.Combine(_root, "PillarsOfEternityII_Data", "exported", "design", "gamedata");
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void LoadGameDataNames_Sweep_RegistersShipFromShipsBundle()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "ships.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
               "DebugName":"SHP_Defiant","ID":"11111111-1111-1111-1111-111111111111"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        Assert.Contains(names["Ship"], e => e.Name == "SHP_Defiant");
    }

    [Fact]
    public void LoadGameDataNames_Sweep_RegistersScheduleFromAiBundle()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "ai.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.ScheduleGameData, Assembly-CSharp",
               "DebugName":"Schedule Townie","ID":"22222222-2222-2222-2222-222222222222"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        Assert.Contains(names["Schedule"], e => e.Name == "Schedule Townie");
    }

    [Fact]
    public void LoadGameDataNames_ExplicitDispositionCleaning_WinsOverSweep()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "factions.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.DispositionGameData, Assembly-CSharp",
               "DebugName":"HonestDisposition","ID":"33333333-3333-3333-3333-333333333333"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        // Explicit registration strips the "Disposition" suffix; the raw sweep would not.
        Assert.Contains(names["Disposition"], e => e.Name == "Honest");
        Assert.DoesNotContain(names["Disposition"], e => e.Name == "HonestDisposition");
    }

    [Fact]
    public void LoadGameDataNames_ItemKind_ExcludesLootLists()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "items.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.ConsumableItemGameData, Assembly-CSharp",
               "DebugName":"Potion","ID":"44444444-4444-4444-4444-444444444444"},
              {"$type":"Game.GameData.LootListGameData, Assembly-CSharp",
               "DebugName":"LL_Quest","ID":"55555555-5555-5555-5555-555555555555"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        Assert.Contains(names["Item"], e => e.Name == "Potion");
        Assert.DoesNotContain(names["Item"], e => e.Name == "LL_Quest");
        Assert.Contains(names["LootList"], e => e.Name == "LL_Quest");
    }

    // ── Inheritance composition ─────────────────────────────────────────────
    // A param whose DataTypeID names a base class accepts every subclass instance
    // (Weapon ⊂ Equippable ⊂ Item; Affliction ⊂ StatusEffect; GenericAbility/Phrase
    // ⊂ ProgressionUnlockable; Attack* ⊂ AttackBase). The sweep's exact-$type
    // buckets must be composed to match.

    [Fact]
    public void LoadGameDataNames_ItemKind_IncludesWeaponsAndConsumables()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "items.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.WeaponGameData, Assembly-CSharp",
               "DebugName":"Sabre","ID":"66666666-6666-6666-6666-666666666666"},
              {"$type":"Game.GameData.ConsumableGameData, Assembly-CSharp",
               "DebugName":"Potion","ID":"77777777-7777-7777-7777-777777777777"},
              {"$type":"Game.GameData.EquippableGameData, Assembly-CSharp",
               "DebugName":"Cloak","ID":"88888888-8888-8888-8888-888888888888"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        Assert.Contains(names["Item"], e => e.Name == "Sabre");
        Assert.Contains(names["Item"], e => e.Name == "Potion");
        Assert.Contains(names["Item"], e => e.Name == "Cloak");
        Assert.Contains(names["Equippable"], e => e.Name == "Sabre"); // Weapon ⊂ Equippable
    }

    [Fact]
    public void LoadGameDataNames_StatusEffectKind_IncludesAfflictions()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "statuseffects.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.AfflictionGameData, Assembly-CSharp",
               "DebugName":"AFF_Sickened","ID":"99999999-9999-9999-9999-999999999999"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        Assert.Contains(names["StatusEffect"], e => e.Name == "AFF_Sickened");
        Assert.Contains(names["Affliction"],   e => e.Name == "AFF_Sickened");
    }

    [Fact]
    public void LoadGameDataNames_ProgressionUnlockable_IsAbilityPlusPhrase()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "abilities.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.GenericAbilityGameData, Assembly-CSharp",
               "DebugName":"Fireball","ID":"aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"},
              {"$type":"Game.GameData.PhraseGameData, Assembly-CSharp",
               "DebugName":"Chant","ID":"bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        Assert.Contains(names["ProgressionUnlockable"], e => e.Name == "Fireball");
        Assert.Contains(names["ProgressionUnlockable"], e => e.Name == "Chant");
    }

    [Fact]
    public void LoadGameDataNames_AttackBase_UnionsAttackSubtypes()
    {
        File.WriteAllText(Path.Combine(GameDataDir(), "attacks.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.AttackMeleeGameData, Assembly-CSharp",
               "DebugName":"Claw","ID":"cccccccc-cccc-cccc-cccc-cccccccccccc"},
              {"$type":"Game.GameData.AttackAOEGameData, Assembly-CSharp",
               "DebugName":"Blast","ID":"dddddddd-dddd-dddd-dddd-dddddddddddd"}
            ]}
            """);
        var names = _provider.LoadGameDataNames();
        Assert.Contains(names["AttackBase"], e => e.Name == "Claw");
        Assert.Contains(names["AttackBase"], e => e.Name == "Blast");
    }

    [Fact]
    public void LoadGameDataNames_RegistersChangeStrength_FromFactionsBundle()
    {
        var gameDataDir = Path.Combine(_root, "PillarsOfEternityII_Data", "exported", "design", "gamedata");
        Directory.CreateDirectory(gameDataDir);
        File.WriteAllText(Path.Combine(gameDataDir, "factions.gamedatabundle"), """
            {"GameDataObjects":[
              {"$type":"Game.GameData.ChangeStrengthGameData, Assembly-CSharp",
               "DebugName":"Major","ID":"e19a6f92-2165-4e34-be10-c65e8de970eb"}
            ]}
            """);

        var names = _provider.LoadGameDataNames();

        Assert.True(names.ContainsKey("ChangeStrength"));
        Assert.Contains(names["ChangeStrength"], e => e.Name == "Major");
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
