using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.GameData;

/// Issue 198: PoE2's DLC conversations live in their own data folders (lax2_exported, …),
/// which the editor never read.
public class Poe2DlcProviderTests : IDisposable
{
    private readonly FakePoe2Game _game = new("en", "de");
    public void Dispose() => _game.Dispose();

    [Fact]
    public void Enumerate_ListsBaseAndDlcConversations_WithTheirDataFolder()
    {
        _game.AddDlcConversation("lax2_exported", "lax02_01_iceberg_dungeon", "lax02_01_cv_ice_carver", "en", "de");

        var files = _game.Provider.EnumerateConversations();

        var base_ = files.Single(f => f.Name == "test_conv");
        Assert.Equal(ConversationFile.BaseDataRoot, base_.DataRoot);
        var dlc = files.Single(f => f.Name == "lax02_01_cv_ice_carver");
        Assert.Equal("lax2_exported", dlc.DataRoot);
        Assert.Equal("lax02_01_iceberg_dungeon", dlc.FolderPath);
        Assert.Equal(["test_conv", "lax02_01_cv_ice_carver"], files.Select(f => f.Name));   // game's folder order
    }

    [Fact]
    public void DlcConversation_LoadsItsOwnText_InEachLanguage()
    {
        _game.AddDlcConversation("lax2_exported", "lax02_01_iceberg_dungeon", "lax02_01_cv_ice_carver", "en", "de");
        var provider = _game.Provider;
        var file = provider.FindConversation("lax02_01_cv_ice_carver")!;

        Assert.Equal("lax02_01_cv_ice_carver en", provider.LoadConversation(file).Strings.Get(1)!.DefaultText);
        provider.Language = "de";
        Assert.Equal("lax02_01_cv_ice_carver de", provider.LoadConversation(file).Strings.Get(1)!.DefaultText);
        Assert.StartsWith(Path.Combine(_game.DataRoot, "lax2_exported", "localized", "de"), provider.GetStringTablePath(file, "de"));
    }

    [Fact]
    public void DlcConversation_MissingLanguage_LoadsWithEmptyText()
    {
        _game.AddDlcConversation("laxi_exported", "laxi", "laxi_00_si_god", "en");   // no "de"
        var provider = _game.Provider;
        provider.Language = "de";

        var conversation = provider.LoadConversation(provider.FindConversation("laxi_00_si_god")!);

        Assert.Single(conversation.Nodes);
        Assert.True(conversation.Strings.IsMissing);
    }

    [Fact]
    public void SavingADlcConversation_WritesOnlyInItsOwnFolder()
    {
        var path = _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");
        var provider = _game.Provider;
        var file = provider.FindConversation("lax02_cv_x")!;
        var before = _game.SnapshotGameData();

        provider.SaveConversation(file, ConversationSnapshotBuilder.Build(provider.LoadConversation(file)));

        var changed = _game.SnapshotGameData()
            .Where(kv => !before.TryGetValue(kv.Key, out var v) || v != kv.Value)
            .Select(kv => kv.Key).ToList();
        Assert.NotEmpty(changed);
        Assert.All(changed, k => Assert.StartsWith("lax2_exported", k));
        Assert.True(File.Exists(path));
    }

    private string GameData(string dataFolder)
    {
        var dir = Path.Combine(_game.DataRoot, dataFolder, "design", "gamedata");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Speakers(params (string Id, string DebugName)[] speakers) =>
        "{\"GameDataObjects\":[" +
        string.Join(",", speakers.Select(s => $"{{\"ID\":\"{s.Id}\",\"DebugName\":\"{s.DebugName}\"}}")) +
        "]}";

    [Fact]
    public void SpeakerNames_IncludeEachDlcsSpeakers_LaterFolderWins()
    {
        File.WriteAllText(Path.Combine(GameData("exported"), "speakers.gamedatabundle"),
            Speakers(("aaaaaaaa-0000-0000-0000-000000000001", "SPK_Base"), ("aaaaaaaa-0000-0000-0000-000000000003", "SPK_Old")));
        File.WriteAllText(Path.Combine(GameData("lax2_exported"), "lax2_speakers.gamedatabundle"),
            Speakers(("aaaaaaaa-0000-0000-0000-000000000002", "SPK_Carver"), ("aaaaaaaa-0000-0000-0000-000000000003", "SPK_New")));

        var names = _game.Provider.LoadSpeakerNames();

        Assert.Equal("Base",   names["aaaaaaaa-0000-0000-0000-000000000001"]);
        Assert.Equal("Carver", names["aaaaaaaa-0000-0000-0000-000000000002"]);
        Assert.Equal("New",    names["aaaaaaaa-0000-0000-0000-000000000003"]);
    }

    [Fact]
    public void Quests_IncludeEachDlcsQuestBundle()
    {
        void Quests(string dataFolder, string file, string id, string name)
        {
            var dir = Path.Combine(_game.DataRoot, dataFolder, "design", "quests");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, file),
                $"{{\"Hash\":0,\"Quests\":[{{\"ID\":\"{id}\",\"Filename\":\"quests/{name}.quest\"}}]}}");
        }
        Quests("exported", "quests.questbundle", "11111111-0000-0000-0000-000000000001", "base_quest");
        Quests("lax1_exported", "lax1_quests.questbundle", "11111111-0000-0000-0000-000000000002", "arena_quest");

        var quests = _game.Provider.LoadGameDataNames()["Quest"];

        Assert.Contains(quests, q => q.Id == "11111111-0000-0000-0000-000000000002");
        Assert.Contains(quests, q => q.Id == "11111111-0000-0000-0000-000000000001");
    }

    [Fact]
    public void ConversationLookup_IncludesDlcConversations()
    {
        _game.AddDlcConversation("lax2_exported", "x", "lax02_cv_x", "en");
        var bundle = Path.Combine(_game.DataRoot, "lax2_exported", "design", "conversations", "x", "lax02_cv_x.conversationbundle");
        File.WriteAllText(bundle, File.ReadAllText(bundle).Replace("{\"Conversations\": [{",
            "{\"Conversations\": [{\"ID\": \"22222222-0000-0000-0000-000000000002\","));

        var conversations = _game.Provider.LoadGameDataNames()["Conversation"];

        Assert.Contains(conversations, c => c.Name == "lax02_cv_x");
    }

    // Final review: lookups list each object once, as the game keeps it (the last folder's copy).
    [Fact]
    public void GameDataLookups_ListAnOverriddenObjectOnce_TheLaterFolderWinning()
    {
        string Ship(string name) => $$"""
            {"GameDataObjects":[{"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
              "DebugName":"{{name}}","ID":"11111111-1111-1111-1111-111111111111"}]}
            """;
        File.WriteAllText(Path.Combine(GameData("exported"), "ships.gamedatabundle"), Ship("SHP_Base"));
        File.WriteAllText(Path.Combine(GameData("lax2_exported"), "lax2_ships.gamedatabundle"), Ship("SHP_Dlc"));

        var ships = _game.Provider.LoadGameDataNames()["Ship"];

        Assert.Equal("SHP_Dlc", Assert.Single(ships).Name);
    }

    [Fact]
    public void ConversationLookup_ListsASharedPathOnce()
    {
        foreach (var dlc in new[] { "lax1_exported", "lax2_exported" })
        {
            var bundle = _game.AddDlcConversation(dlc, "lax", "lax_cv_craftsperson", "en");
            File.WriteAllText(bundle, File.ReadAllText(bundle).Replace("{\"Conversations\": [{",
                "{\"Conversations\": [{\"ID\": \"745a9191-fd3e-4b9c-be41-7cba7f54ae86\","));
        }

        var conversations = _game.Provider.LoadGameDataNames()["Conversation"];

        Assert.Single(conversations, c => c.Name == "lax_cv_craftsperson");
    }

    // ── Several folders, one conversation (issue 198, from the game's own loaders) ──────────
    // ConversationDataManager loads a relative path from every data folder in order and keeps
    // the last; StringTableManager looks each line up from the last folder back to the first.

    private string TextTable(string dataFolder, string lang, string relative, params (int Id, string Text)[] lines)
    {
        var path = Path.Combine(_game.DataRoot, dataFolder, "localized", lang, "text", "conversations", relative + ".stringtable");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "<StringTableFile><Entries>" +
            string.Concat(lines.Select(l => $"<Entry><ID>{l.Id}</ID><DefaultText>{l.Text}</DefaultText><FemaleText></FemaleText></Entry>")) +
            "</Entries></StringTableFile>");
        return path;
    }

    [Fact]
    public void ASharedPath_IsListedOnce_FromTheLastFolderThatHasIt()
    {
        foreach (var dlc in new[] { "lax1_exported", "lax2_exported", "lax3_exported" })
            _game.AddDlcConversation(dlc, "lax", "lax_cv_craftsperson", "en");

        var file = Assert.Single(_game.Provider.EnumerateConversations(), f => f.Name == "lax_cv_craftsperson");

        Assert.Equal("lax3_exported", file.DataRoot);
        Assert.Equal(Path.Combine(_game.DataRoot, "lax3_exported", "design", "conversations", "lax", "lax_cv_craftsperson.conversationbundle"),
                     file.ConversationPath);
    }

    [Fact]
    public void Text_IsMergedPerLine_TheLaterFolderWinning()
    {
        // A DLC table for a base-game conversation: line 1 overridden, line 2 added.
        TextTable("lax2_exported", "en", "test_conv", (1, "dlc line"), (2, "added line"));
        var provider = _game.Provider;

        var strings = provider.LoadConversation(provider.FindConversation("test_conv")!).Strings;

        Assert.Equal("dlc line", strings.Get(1)!.DefaultText);
        Assert.Equal("added line", strings.Get(2)!.DefaultText);
    }

    [Fact]
    public void TextIsWritten_ToTheLastFolderWithATable_ElseTheBundlesFolder()
    {
        var dlcTable = TextTable("lax2_exported", "en", "test_conv", (1, "dlc line"));
        var provider = _game.Provider;
        var file = provider.FindConversation("test_conv")!;

        Assert.Equal(dlcTable, provider.GetStringTablePath(file, "en"));     // a DLC table wins every line it has
        Assert.Equal(_game.StPath("de"), provider.GetStringTablePath(file, "de"));   // no DLC table in de
    }

    [Fact]
    public void TheLastBundleWithoutText_TakesItsTextFromAnEarlierFolder()
    {
        // lax_re_city_ambush_mag: the bundle is in lax1, lax2 and lax3; English text only in lax1 and lax2.
        _game.AddDlcConversation("lax1_exported", "lax", "lax_re_city_ambush_mag", "en");
        _game.AddDlcConversation("lax2_exported", "lax", "lax_re_city_ambush_mag", "en");
        _game.AddDlcConversation("lax3_exported", "lax", "lax_re_city_ambush_mag");
        var provider = _game.Provider;
        var file = provider.FindConversation("lax_re_city_ambush_mag")!;

        Assert.Equal("lax3_exported", file.DataRoot);
        Assert.Equal("lax_re_city_ambush_mag en", provider.LoadConversation(file).Strings.Get(1)!.DefaultText);
        Assert.StartsWith(Path.Combine(_game.DataRoot, "lax2_exported"), provider.GetStringTablePath(file, "en"));
    }

    [Fact]
    public void NoTableAnywhere_IsMissing()
    {
        _game.AddDlcConversation("laxi_exported", "laxi", "laxi_00_si_god");   // no text in any language
        var provider = _game.Provider;

        Assert.True(provider.LoadConversation(provider.FindConversation("laxi_00_si_god")!).Strings.IsMissing);
    }
}
