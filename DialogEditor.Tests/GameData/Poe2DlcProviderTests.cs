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
}
