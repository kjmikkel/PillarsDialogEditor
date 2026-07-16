using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class FactionCheckClassifierTests : IDisposable
{
    public void Dispose() => GameDataNameService.Clear();

    private static ConditionLeaf Leaf(string full, params string[] args) =>
        new(full, args, Not: false, Operator: "And");

    [Fact]
    public void Classify_Poe1DispositionEqual_ReturnsDispositionWithAxisValue()
    {
        var leaf = Leaf("Boolean DispositionEqual(Axis, Rank)", "Benevolent", "2");
        var result = FactionCheckClassifier.Classify(leaf, "poe1", ConditionCatalogue.Instance);
        Assert.NotNull(result);
        Assert.Equal(FactionCheckDomain.Disposition, result!.Domain);
        Assert.Equal("Benevolent", result.RawValue);
    }

    [Fact]
    public void Classify_Poe2IsReputation_ReturnsReputationWithFactionGuid()
    {
        var leaf = Leaf("Boolean IsReputation(Guid, RankType, Int32, Operator)",
            "faction-guid", "Good", "2", "GreaterThan");
        var result = FactionCheckClassifier.Classify(leaf, "poe2", ConditionCatalogue.Instance);
        Assert.NotNull(result);
        Assert.Equal(FactionCheckDomain.Reputation, result!.Domain);
        Assert.Equal("faction-guid", result.RawValue);
    }

    [Fact]
    public void Classify_NonFactionCondition_ReturnsNull()
    {
        var leaf = Leaf("Boolean IsGlobalValue(String, Operator, Int32)", "g", "EqualTo", "1");
        Assert.Null(FactionCheckClassifier.Classify(leaf, "poe2", ConditionCatalogue.Instance));
    }

    [Fact]
    public void PossibleValues_Reputation_ReturnsRegisteredFactions()
    {
        GameDataNameService.Register("Faction", new[]
        {
            new NamedEntry("Huana — h-guid", "h-guid"),
            new NamedEntry("Vailian Trading Company — v-guid", "v-guid"),
        });
        var values = FactionCheckClassifier.PossibleValues(
            FactionCheckDomain.Reputation, "poe2", ConditionCatalogue.Instance);
        Assert.Equal(2, values.Count);
        Assert.Contains(values, v => v.StoredValue == "h-guid");
    }
}
