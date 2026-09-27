using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Install;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Patch.Install;

public class PatchInstallerTests
{
    private static InstallResult Install(FakePoe2Game g, params DialogProject[] mods) =>
        PatchInstaller.Install(g.Provider, g.Root, mods.Select(m => new InstallEntry(m)).ToList(), new InstallOptions());

    [Fact]
    public void Install_SameModTwice_DoesNotStack()
    {
        // Issue #76: before the clean-base apply, the second run patched already-modded
        // files and failed with PatchConflictException (ExternalVO was already "a").
        using var game = new FakePoe2Game();
        var mod = FakePoe2Game.ExternalVoMod("A", "a");

        Install(game, mod);
        var result = Install(game, mod);

        Assert.IsType<InstallResult.Applied>(result);
        Assert.Equal("a", game.ReadExternalVo());
    }

    [Fact]
    public void Install_ReorderedLoadOrder_MatchesFreshApply()
    {
        var a = FakePoe2Game.ExternalVoMod("A", "a");
        var b = FakePoe2Game.ExternalVoMod("B", "b");
        using var reapplied = new FakePoe2Game();
        using var fresh     = new FakePoe2Game();

        Install(reapplied, a, b);
        Install(reapplied, b, a);
        Install(fresh, b, a);

        Assert.Equal("a", reapplied.ReadExternalVo());
        Assert.Equal(fresh.SnapshotGameData(), reapplied.SnapshotGameData());
    }

    [Fact]
    public void Install_RemovingModFromList_RevertsIt()
    {
        var a = FakePoe2Game.ExternalVoMod("A", "a");
        var b = FakePoe2Game.ExternalVoMod("B", "b");
        using var reapplied = new FakePoe2Game();
        using var fresh     = new FakePoe2Game();

        Install(reapplied, a, b);
        Install(reapplied, a);
        Install(fresh, a);

        Assert.Equal(fresh.SnapshotGameData(), reapplied.SnapshotGameData());
    }

    [Fact]
    public void Install_WritesTranslationsForEveryInstalledLanguage()
    {
        // The Patch Manager used to drop translations; the shared installer must not.
        using var game = new FakePoe2Game("en", "fr");
        var patch = new ConversationPatch("test_conv", ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "modded en", "")],
                ["fr"] = [new NodeTranslation(1, "modded fr", "")],
            },
        };

        Install(game, DialogProject.Empty("T").WithPatch(patch));

        Assert.Contains("modded en", File.ReadAllText(game.StPath("en")));
        Assert.Contains("modded fr", File.ReadAllText(game.StPath("fr")));
    }

    [Fact]
    public void Install_NewConversation_IsCreated()
    {
        using var game = new FakePoe2Game();
        var added = new NodeEditSnapshot(1, false, SpeakerCategory.Npc, "spk", "lst",
            "", "", "Conversation", "None", "", "", "", false, false, [], [], []);
        var project = DialogProject.Empty("N")
            .WithNewConversation("brand_new")
            .WithPatch(new ConversationPatch("brand_new", ConversationPatch.CurrentSchemaVersion, [added], [], []));

        var result = Assert.IsType<InstallResult.Applied>(Install(game, project));

        Assert.Equal(1, result.ConversationsPatched);
        Assert.True(File.Exists(game.ConvPath("brand_new")));
    }
}
