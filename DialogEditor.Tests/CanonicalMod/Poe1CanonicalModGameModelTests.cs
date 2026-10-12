using DialogEditor.Tests.GameData;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138, spec §3 "Game model": the installed PoE1 canonical mod as the game itself
/// reads it. Each file goes through the install's own OEIFormats XmlSerializer (loaded at
/// run time, never redistributed): a file the game cannot load throws, and anything the
/// game ignores (an element its type does not declare) drops out. The scenario's own
/// expectations then run on what is left, so a feature passes only if the game sees it.
[Trait("Category", "GameData")]
public sealed class Poe1CanonicalModGameModelTests(BuiltPoe1Mod mod) : IClassFixture<BuiltPoe1Mod>
{
    private static string Install => GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe1Variable);

    /// A copy of the installed game folder with every conversation and stringtable replaced
    /// by the game's own canonical form of it.
    private string GameSeen()
    {
        var conv = RoundTripChecks.Poe1GameSerializer(Install);
        var st   = RoundTripChecks.Poe1GameStringTableSerializer(Install);
        var copy = Path.Combine(mod.Root, $"game-seen {Guid.NewGuid():N}");
        GoldenDiff.CopyTree(mod.GameRoot, copy);
        foreach (var file in Directory.EnumerateFiles(copy, "*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".conversation") || f.EndsWith(".stringtable")))
        {
            var serializer = file.EndsWith(".conversation") ? conv : st;
            File.WriteAllText(file, RoundTripChecks.Poe1GameCanonical(serializer, File.ReadAllText(file)));
        }
        return copy;
    }

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void EveryInstalledFile_LoadsInTheGamesOwnModel() => GameSeen();   // throws on the first file the game rejects

    // Steps with a KnownIssue fail in the editor's own view too; Poe1ScenarioStepTests
    // carries their skipped tests. GameOnlyIssues are steps that hold for the editor's
    // parser but not for the game (step tag -> issue), each with its own skipped test below.
    private static readonly Dictionary<string, string> GameOnlyIssues = new()
    {
        ["links.display-values"] = "#164",
    };

    public static TheoryData<string> Tags =>
        [.. Poe1Scenario.Steps.Where(s => s.KnownIssue is null && !GameOnlyIssues.ContainsKey(s.Tag)).Select(s => s.Tag)];

    [GameInstallTheory(GameInstallFactAttribute.Poe1Variable)]
    [MemberData(nameof(Tags))]
    public void Step_HoldsAsTheGameSeesIt(string tag)
    {
        var game = CanonicalGame.Poe1;
        var view = ModView.FromGame(game.Provider(GameSeen()), game.Conversations, game.Languages);
        Poe1Scenario.Steps.Single(s => s.Tag == tag).Expect(view);
    }

    [Fact(Skip = "#164: new PoE1 links lack xsi:type, so the game drops their display value")]
    public void Step_LinksDisplayValues_AsTheGameSeesIt() => Step_HoldsAsTheGameSeesIt("links.display-values");

    [Fact]
    public void EveryGameOnlyIssue_NamesARealStepAndHasItsOwnSkippedTest() =>
        Assert.Equal(["links.display-values"], GameOnlyIssues.Keys.Where(k => Poe1Scenario.Steps.Any(s => s.Tag == k)));
}
