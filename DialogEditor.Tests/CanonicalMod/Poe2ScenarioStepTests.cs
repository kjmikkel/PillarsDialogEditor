namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138: every scenario step's own expectation, checked against the PoE2 canonical
/// mod after the patcher has written it into a fresh copy of the canonical install. One
/// test case per step, so a failure names the feature.
[Trait("Category", "Canonical")]
public class Poe2ScenarioStepTests(BuiltPoe2Mod mod) : IClassFixture<BuiltPoe2Mod>
{
    public static TheoryData<string> Tags => [.. Poe2Scenario.Steps.Select(s => s.Tag)];

    [Theory]
    [MemberData(nameof(Tags))]
    public void Step_HoldsAfterInstall(string tag) =>
        Poe2Scenario.Steps.Single(s => s.Tag == tag).Expect(mod.Applied);

    [Fact]
    public void Tags_AreUnique() =>
        Assert.Equal(Poe2Scenario.Steps.Count, Poe2Scenario.Steps.Select(s => s.Tag).Distinct().Count());
}
