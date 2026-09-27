using DialogEditor.Patch.Install;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Patch.Install;

public class PatcherBackupInfoTests
{
    [Fact]
    public void TryOpen_NoManifest_ReturnsNull()
    {
        using var game = new FakePoe2Game();
        Assert.Null(PatcherBackupInfo.TryOpen(game.Root));
    }

    [Fact]
    public void CoveredPaths_ReturnsOnlyManagedFiles()
    {
        using var game = new FakePoe2Game();
        PatchInstaller.Install(game.Provider, game.Root,
            [new InstallEntry(FakePoe2Game.ExternalVoMod("A", "a"))], new InstallOptions());

        var info = PatcherBackupInfo.TryOpen(game.Root)!;

        Assert.Equal([game.ConvPath()], info.CoveredPaths([game.ConvPath(), game.StPath("en")]));
    }

    [Fact]
    public void CorruptManifest_IsUnknown_AndCoversEverything()
    {
        using var game = new FakePoe2Game();
        var dir = Path.Combine(game.Root, PatcherBackupStore.FolderName);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, PatcherBackupStore.ManifestFileName), "garbage");

        var info = PatcherBackupInfo.TryOpen(game.Root)!;

        Assert.True(info.IsUnknown);
        Assert.Equal([game.StPath("en")], info.CoveredPaths([game.StPath("en")]));
    }
}
