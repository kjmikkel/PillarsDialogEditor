using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Conditions;

public class LinkFiringTests
{
    // g is set to 0 in State(), so "g EqualTo 1" is definitely False and "g EqualTo 0" definitely True.
    private static ConditionLeaf Global(string name, string value)
        => new("Boolean IsGlobalValue(String, Operator, Int32)", [name, "EqualTo", value], Not: false, Operator: "And");

    private static IReadOnlyList<ConditionNode> False_() => [Global("g", "1")];
    private static IReadOnlyList<ConditionNode> True_() => [Global("g", "0")];
    // "h" is never set, so this reads an unknown fact.
    private static IReadOnlyList<ConditionNode> Unknown_() => [Global("h", "1")];

    private static GameState State()
    {
        var s = new GameState();
        s.Globals["g"] = 0;
        return s;
    }

    [Fact]
    public void NoFacts_NeverBlocks()
        => Assert.False(LinkFiring.CannotFire(False_(), False_(), new GameState(), "poe2"));

    [Fact]
    public void LinkFalse_CannotFire()
        => Assert.True(LinkFiring.CannotFire(False_(), [], State(), "poe2"));

    [Fact]
    public void TargetFalse_WithEmptyLinkConditions_CannotFire()
        => Assert.True(LinkFiring.CannotFire([], False_(), State(), "poe2"));

    [Fact]
    public void Unknown_DoesNotBlock()
        => Assert.False(LinkFiring.CannotFire(Unknown_(), Unknown_(), State(), "poe2"));

    [Fact]
    public void TrueAndTrue_CanFire()
        => Assert.False(LinkFiring.CannotFire(True_(), True_(), State(), "poe2"));
}
