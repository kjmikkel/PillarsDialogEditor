using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.GameData;

/// Issue 198: PoE2 keeps each DLC's data in its own folder; the editor reads them in the
/// game's own order (ProductConfiguration.PackageDataFolders).
public class Poe2DataRootsTests : IDisposable
{
    private readonly string _game = Path.Combine(Path.GetTempPath(), $"roots_{Guid.NewGuid():N}");

    public void Dispose() { try { Directory.Delete(_game, recursive: true); } catch { /* best-effort */ } }

    private void Make(params string[] folders)
    {
        foreach (var f in folders) Directory.CreateDirectory(Path.Combine(_game, Poe2DataRoots.DataFolder, f));
    }

    [Fact]
    public void All_IsTheGamesOrder()
    {
        Assert.Equal(
            ["exported", "bb_exported", "lax1_exported", "lax2_exported", "lax3_exported", "lax4_exported",
             "laxa_exported", "laxb_exported", "laxc_exported", "laxd_exported", "unused_exported",
             "laxe_exported", "laxf_exported", "laxg_exported", "laxh_exported", "laxi_exported"],
            Poe2DataRoots.All.Select(r => r.Folder));
        Assert.All(Poe2DataRoots.All.Skip(1), r => Assert.Equal(r.Folder, r.Prefix + "exported"));
    }

    [Fact]
    public void Existing_ReturnsOnlyFoldersThatExist_InTheGamesOrder()
    {
        Make("laxi_exported", "exported", "lax2_exported");
        var roots = Poe2DataRoots.Existing(_game);
        Assert.Equal(["exported", "lax2_exported", "laxi_exported"], roots.Select(r => r.Folder));
        Assert.Equal(Path.Combine(_game, Poe2DataRoots.DataFolder, "lax2_exported"), roots[1].Path);
        Assert.Equal("lax2_", roots[1].Prefix);
    }

    [Theory]
    [InlineData("exported", null)]
    [InlineData("lax2_exported", "lax2")]
    [InlineData("laxi_exported", "laxi")]
    [InlineData("bb_exported", "bb")]
    [InlineData("not_a_data_folder", null)]
    public void KeyOf_IsThePrefixWithoutItsUnderscore(string folder, string? key)
    {
        Assert.Equal(key, Poe2DataRoots.KeyOf(folder));
        if (key is not null) Assert.Equal(key, new Poe2DataRoot(folder, key + "_", "").Key);
    }
}
