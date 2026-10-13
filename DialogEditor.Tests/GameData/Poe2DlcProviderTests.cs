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
}
