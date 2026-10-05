using DialogEditor.Patch;
using DialogEditor.Patch.Install;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Patch.Install;

public class PatchInstallerSafetyTests
{
    private static InstallResult Install(FakePoe2Game g, InstallOptions o, params InstallEntry[] e) =>
        PatchInstaller.Install(g.Provider, g.Root, e, o);

    private static InstallEntry Entry(DialogProject p, string? vo = null) => new(p, vo);

    [Fact]
    public void Restore_GivesByteIdenticalOriginals_AndRemovesBackupFolder()
    {
        using var game = new FakePoe2Game("en", "fr");
        var before = game.SnapshotGameData();
        var vo = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(vo, "existing.wem"), [9, 9]);   // overwrites a vanilla file
        File.WriteAllBytes(Path.Combine(vo, "added.wem"),    [7]);      // creates a new one
        try
        {
            var r = Assert.IsType<InstallResult.Applied>(
                Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a"), vo)));
            Assert.Equal(2, r.VoFilesCopied);
            Assert.NotEqual(before, game.SnapshotGameData());

            var restore = PatchInstaller.Restore(game.Root);

            Assert.Empty(restore.Skipped);
            Assert.Equal(before, game.SnapshotGameData());
            Assert.False(PatchInstaller.HasInstalledMods(game.Root));
        }
        finally { Directory.Delete(vo, true); }
    }

    [Fact]
    public void Restore_RemovesFoldersTheInstallCreated()
    {
        // VO in nested folders the game doesn't have: "Remove all mods" must take the
        // folders away too, not just the files (issue 125).
        using var game = new FakePoe2Game("en");
        var before = game.SnapshotGameData();
        var vo = Directory.CreateTempSubdirectory().FullName;
        Directory.CreateDirectory(Path.Combine(vo, "a", "b"));
        File.WriteAllBytes(Path.Combine(vo, "a", "b", "added.wem"), [7]);
        try
        {
            Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a"), vo));
            Assert.True(Directory.Exists(Path.Combine(game.VoDir, "a", "b")));

            PatchInstaller.Restore(game.Root);

            Assert.Equal(before, game.SnapshotGameData());
        }
        finally { Directory.Delete(vo, true); }
    }

    [Fact]
    public void Install_ExternallyChangedFile_StopsWithoutWriting()
    {
        using var game = new FakePoe2Game();
        Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a")));
        File.WriteAllText(game.ConvPath(), FakePoe2Game.OneNodeBundle.Replace("aaaa-0000", "game-update"));
        var before = game.SnapshotGameData();

        var result = Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("B", "b")));

        var ext = Assert.IsType<InstallResult.ExternalChanges>(result);
        Assert.Contains(ext.Paths, p => p.EndsWith("test_conv.conversationbundle"));
        Assert.Equal(before, game.SnapshotGameData());
    }

    [Fact]
    public void Install_AcceptCurrentFiles_AdoptsThemAndApplies()
    {
        using var game = new FakePoe2Game();
        Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a")));
        var updated = FakePoe2Game.OneNodeBundle.Replace("aaaa-0000", "game-update");
        File.WriteAllText(game.ConvPath(), updated);

        var result = Install(game, new InstallOptions(AcceptCurrentFiles: true),
                             Entry(FakePoe2Game.ExternalVoMod("B", "b")));

        Assert.IsType<InstallResult.Applied>(result);
        Assert.Equal("b", game.ReadExternalVo());
        PatchInstaller.Restore(game.Root);
        Assert.Equal(updated, File.ReadAllText(game.ConvPath()));
    }

    [Fact]
    public void Install_PatchConflict_LeavesGameClean()
    {
        using var game = new FakePoe2Game();
        var before = game.SnapshotGameData();
        // Expects ExternalVO "zzz" but the game holds "" → conflict.
        var conflicting = DialogProject.Empty("Bad").WithPatch(new ConversationPatch("test_conv",
            ConversationPatch.CurrentSchemaVersion, [], [],
            [new NodeModification(1, new Dictionary<string, FieldChange>
                { ["ExternalVO"] = new("\"zzz\"", "\"x\"") }, [], [])]));

        Assert.Throws<PatchConflictException>(() =>
            Install(game, new InstallOptions(), Entry(conflicting)));

        Assert.Equal(before, game.SnapshotGameData());
    }

    [Fact]
    public void Install_CrashPartway_ManifestCoversEverythingAndRestoreRecovers()
    {
        using var game = new FakePoe2Game("en", "fr");
        var before = game.SnapshotGameData();
        var vo = Directory.CreateTempSubdirectory().FullName;
        File.WriteAllBytes(Path.Combine(vo, "added.wem"), [7]);
        var writes = 0;
        var options = new InstallOptions
        {
            BeforeWrite = path => { if (++writes == 3) throw new IOException("disk full (simulated)"); },
        };
        try
        {
            Assert.Throws<IOException>(() =>
                Install(game, options, Entry(FakePoe2Game.ExternalVoMod("A", "a"), vo)));

            PatchInstaller.Restore(game.Root);
            Assert.Equal(before, game.SnapshotGameData());
        }
        finally { Directory.Delete(vo, true); }
    }

    [Fact]
    public void Install_And_Restore_RefuseCorruptManifest()
    {
        using var game = new FakePoe2Game();
        var manifest = Path.Combine(game.Root, "PillarsDialogPatcher", "manifest.json");
        Directory.CreateDirectory(Path.GetDirectoryName(manifest)!);
        File.WriteAllText(manifest, "garbage");

        Assert.Throws<PatcherBackupCorruptException>(() =>
            Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a"))));
        Assert.Throws<PatcherBackupCorruptException>(() => PatchInstaller.Restore(game.Root));
        Assert.Equal("garbage", File.ReadAllText(manifest));
    }

    [Fact]
    public void Plan_ReportsWorkWithoutWriting()
    {
        using var game = new FakePoe2Game();
        Install(game, new InstallOptions(), Entry(FakePoe2Game.ExternalVoMod("A", "a")));
        var before = game.SnapshotGameData();

        var plan = PatchInstaller.Plan(game.Root, [Entry(FakePoe2Game.ExternalVoMod("B", "b"))]);

        Assert.Equal(1, plan.ConversationsToPatch);
        Assert.True(plan.FilesToRestoreFirst > 0);
        Assert.Empty(plan.ExternallyChanged);
        Assert.Equal(before, game.SnapshotGameData());
    }

    [Fact]
    public void VoRoot_IsNullForPoe1()
    {
        var poe1 = new DialogEditor.Core.GameData.Poe1GameDataProvider(@"C:\x");
        Assert.Null(PatchInstaller.VoRoot(poe1, @"C:\x"));
    }
}
