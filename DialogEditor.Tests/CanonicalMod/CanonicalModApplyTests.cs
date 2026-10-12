namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138, spec §3: each game's checked-in canonical mod applied through each delivery path
/// a player or author uses — dialog-patcher, the Patch Manager, Test Patch (F5) — into a
/// fresh copy of the canonical install, then verified.
[Trait("Category", "Canonical")]
public sealed class CanonicalModApplyTests : IDisposable
{
    private readonly DeliveryPaths _paths = new();
    public void Dispose() => _paths.Dispose();

    public static TheoryData<string, string> Routes => new()
    {
        { "poe1", "patcher" }, { "poe1", "patchmanager" }, { "poe1", "testpatch" },
        { "poe2", "patcher" }, { "poe2", "patchmanager" }, { "poe2", "testpatch" },
    };

    private async Task<string> ApplyAsync(CanonicalGame game, string route)
    {
        var root = _paths.FreshInstall(game.Id);
        switch (route)
        {
            case "patcher":      DeliveryPaths.ViaPatcher(root, game.Pack); break;
            case "patchmanager": await DeliveryPaths.ViaPatchManagerAsync(root, game.Pack); break;
            default:             await DeliveryPaths.ViaTestPatchAsync(game, root); break;
        }
        return root;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryStep_HoldsAfter(string gameId, string route)
    {
        var game = CanonicalGame.For(gameId);
        var root = await ApplyAsync(game, route);
        var view = ModView.FromGame(game.Provider(root), game.Conversations, game.Languages);
        var failures = new List<string>();
        foreach (var step in game.Steps.Where(s => s.KnownIssue is null))
            try { step.Expect(view); } catch (Exception ex) { failures.Add($"{step.Tag}: {ex.Message}"); }
        Assert.Empty(failures);
    }

    [Theory]
    [InlineData("poe1")]
    [InlineData("poe2")]
    public async Task AllThreeRoutes_WriteIdenticalGameFiles(string gameId)
    {
        var game         = CanonicalGame.For(gameId);
        var patcher      = DeliveryPaths.GameFiles(await ApplyAsync(game, "patcher"));
        var patchManager = DeliveryPaths.GameFiles(await ApplyAsync(game, "patchmanager"));
        var testPatch    = DeliveryPaths.GameFiles(await ApplyAsync(game, "testpatch"));
        var vsPatchManager = DeliveryPaths.Describe(patcher, patchManager);
        var vsTestPatch    = DeliveryPaths.Describe(patcher, testPatch);
        Assert.True(vsPatchManager is null, "patcher vs Patch Manager:\n" + vsPatchManager);
        Assert.True(vsTestPatch is null,    "patcher vs Test Patch:\n" + vsTestPatch);
    }

    // Review Focus 2: every install starts from the originals, so applying twice changes nothing.
    [Theory]
    [InlineData("poe1")]
    [InlineData("poe2")]
    public async Task ApplyingTwice_GivesTheSameFiles(string gameId)
    {
        var game = CanonicalGame.For(gameId);
        var root = _paths.FreshInstall(game.Id);
        await DeliveryPaths.ViaPatchManagerAsync(root, game.Pack);
        var once = DeliveryPaths.GameFiles(root);
        await DeliveryPaths.ViaPatchManagerAsync(root, game.Pack);
        Assert.Equal(once, DeliveryPaths.GameFiles(root));
    }

    // Review Focus 4: a mod language the install lacks is skipped; the rest still applies.
    [Theory]
    [InlineData("poe1")]
    [InlineData("poe2")]
    public async Task MissingModLanguage_IsSkipped_AndEnglishStillApplies(string gameId)
    {
        var game  = CanonicalGame.For(gameId);
        var root  = _paths.FreshInstall(game.Id);
        var deDir = Directory.EnumerateDirectories(root, "de", SearchOption.AllDirectories).Single();
        Directory.Delete(deDir, true);

        await DeliveryPaths.ViaPatchManagerAsync(root, game.Pack);

        var view = ModView.FromGame(game.Provider(root), game.Conversations, ["en"]);
        Assert.Equal("A new closing line, written by the canonical mod.", view.Text(game.Edited, "en", 11)!.DefaultText);
        Assert.False(Directory.Exists(deDir));
    }
}
