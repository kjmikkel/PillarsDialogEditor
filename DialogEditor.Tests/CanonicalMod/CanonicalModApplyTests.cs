using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138, spec §3: the checked-in PoE2 canonical mod applied through each delivery path
/// a player or author uses — dialog-patcher, the Patch Manager, Test Patch (F5) — into a
/// fresh copy of the canonical install, then verified.
[Trait("Category", "Canonical")]
public sealed class CanonicalModApplyTests : IDisposable
{
    private readonly DeliveryPaths _paths = new();
    public void Dispose() => _paths.Dispose();

    private static string Pack    => Path.Combine(CanonicalModBuilder.GoldenDir("poe2"), CanonicalModBuilder.ProjectName + ".dialogpack");
    private static string Project => Path.Combine(CanonicalModBuilder.GoldenDir("poe2"), CanonicalModBuilder.ProjectName + ".dialogproject");

    public static TheoryData<string> Routes => ["patcher", "patchmanager", "testpatch"];

    private async Task<string> ApplyAsync(string route)
    {
        var root = _paths.FreshInstall("poe2");
        switch (route)
        {
            case "patcher":      DeliveryPaths.ViaPatcher(root, Pack); break;
            case "patchmanager": await DeliveryPaths.ViaPatchManagerAsync(root, Pack); break;
            default:             await _paths.ViaTestPatchAsync(root, Project); break;
        }
        return root;
    }

    [Theory]
    [MemberData(nameof(Routes))]
    public async Task EveryStep_HoldsAfter(string route)
    {
        var root = await ApplyAsync(route);
        var view = ModView.FromGame(new Poe2GameDataProvider(root), Poe2Scenario.Conversations, ["en", "de"]);
        var failures = new List<string>();
        foreach (var step in Poe2Scenario.Steps.Where(s => s.KnownIssue is null))
            try { step.Expect(view); } catch (Exception ex) { failures.Add($"{step.Tag}: {ex.Message}"); }
        Assert.Empty(failures);
    }

    [Fact]
    public async Task AllThreeRoutes_WriteIdenticalGameFiles()
    {
        var patcher      = DeliveryPaths.GameFiles(await ApplyAsync("patcher"));
        var patchManager = DeliveryPaths.GameFiles(await ApplyAsync("patchmanager"));
        var testPatch    = DeliveryPaths.GameFiles(await ApplyAsync("testpatch"));
        var vsPatchManager = DeliveryPaths.Describe(patcher, patchManager);
        var vsTestPatch    = DeliveryPaths.Describe(patcher, testPatch);
        Assert.True(vsPatchManager is null, "patcher vs Patch Manager:\n" + vsPatchManager);
        Assert.True(vsTestPatch is null,    "patcher vs Test Patch:\n" + vsTestPatch);
    }

    // Review Focus 2: every install starts from the originals, so applying twice changes nothing.
    [Fact]
    public async Task ApplyingTwice_GivesTheSameFiles()
    {
        var root = _paths.FreshInstall("poe2");
        await DeliveryPaths.ViaPatchManagerAsync(root, Pack);
        var once = DeliveryPaths.GameFiles(root);
        await DeliveryPaths.ViaPatchManagerAsync(root, Pack);
        Assert.Equal(once, DeliveryPaths.GameFiles(root));
    }

    // Review Focus 4: a mod language the install lacks is skipped; the rest still applies.
    [Fact]
    public async Task MissingModLanguage_IsSkipped_AndEnglishStillApplies()
    {
        var root  = _paths.FreshInstall("poe2");
        var deDir = Path.Combine(root, "PillarsOfEternityII_Data", "exported", "localized", "de");
        Directory.Delete(deDir, true);

        await DeliveryPaths.ViaPatchManagerAsync(root, Pack);

        var view = ModView.FromGame(new Poe2GameDataProvider(root), Poe2Scenario.Conversations, ["en"]);
        Assert.Equal("A new closing line, written by the canonical mod.", view.Text(Poe2Scenario.Edited, "en", 11)!.DefaultText);
        Assert.False(Directory.Exists(deDir));
    }
}
