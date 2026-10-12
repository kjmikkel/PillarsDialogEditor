using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138: every PoE1 scenario step's own expectation, checked against the PoE1 canonical
/// mod after the patcher has written it into a fresh copy of the canonical install. One
/// test case per step, so a failure names the feature.
[Trait("Category", "Canonical")]
public class Poe1ScenarioStepTests(BuiltPoe1Mod mod) : IClassFixture<BuiltPoe1Mod>
{
    // Steps with a KnownIssue are left out here and get their own skipped test below.
    public static TheoryData<string> Tags => [.. Poe1Scenario.Steps.Where(s => s.KnownIssue is null).Select(s => s.Tag)];

    [Theory]
    [MemberData(nameof(Tags))]
    public void Step_HoldsAfterInstall(string tag) =>
        Poe1Scenario.Steps.Single(s => s.Tag == tag).Expect(mod.Applied);

    [Fact]
    public void EveryKnownIssueStep_HasItsOwnSkippedTest() =>
        Assert.Empty(Poe1Scenario.Steps.Where(s => s.KnownIssue is not null).Select(s => s.Tag));

    /// Every expectation must FAIL on the unmodified canonical install: an expectation that
    /// already holds there proves nothing about the edit (the red half of red/green).
    [Fact]
    public void EveryExpectation_FailsOnTheUnmodifiedFixture()
    {
        var root = Path.Combine(Path.GetTempPath(), $"canonical vanilla {Guid.NewGuid():N}");
        try
        {
            CanonicalFixture.CopyTo("poe1", root);
            var vanilla = ModView.FromGame(CanonicalGame.Poe1.Provider(root), [Poe1Scenario.Edited], ["en", "de"]);
            var holding = Poe1Scenario.Steps
                .Where(s => { try { s.Expect(vanilla); return true; } catch (Exception) { return false; } })
                .Select(s => s.Tag);
            Assert.Empty(holding);
        }
        finally { try { Directory.Delete(root, true); } catch (Exception) { /* best-effort */ } }
    }

    [Fact]
    public void Tags_AreUnique() =>
        Assert.Equal(Poe1Scenario.Steps.Count, Poe1Scenario.Steps.Select(s => s.Tag).Distinct().Count());
}
