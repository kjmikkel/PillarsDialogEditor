using DialogEditor.Core.Models;
using DialogEditor.Core.Search;

namespace DialogEditor.Tests.Search;

public class CatalogueMatchTests
{
    private static CatalogueMatch Match(string full, params ParameterPin[] pins) => new(full, pins);

    [Fact]
    public void Matches_MethodIdentity_CaseInsensitive_DifferentSignatureFails()
    {
        var m = Match("Boolean IsReputation(Guid, RankType, Int32, Operator)");
        Assert.True(m.Matches("boolean isreputation(guid, ranktype, int32, operator)", new[] { "a" }));
        Assert.False(m.Matches("Boolean IsReputation(Guid, Axis, Int32)", new[] { "a" }));  // PoE1 overload
    }

    [Fact]
    public void Matches_PinnedParam_MatchesAndMisses()
    {
        var m = Match("Boolean IsDisposition(Guid, Rank, Operator)",
            ParameterPin.Pin("benevolent"), ParameterPin.Wildcard, ParameterPin.Wildcard);
        Assert.True(m.Matches("Boolean IsDisposition(Guid, Rank, Operator)", new[] { "Benevolent", "2", "GT" }));
        Assert.False(m.Matches("Boolean IsDisposition(Guid, Rank, Operator)", new[] { "Cruel", "2", "GT" }));
    }

    [Fact]
    public void Matches_WildcardOnly_MatchesAnyCallOfMethod()
    {
        var m = Match("Void SetGlobalValue(String, Int32)");
        Assert.True(m.Matches("Void SetGlobalValue(String, Int32)", new[] { "x", "1" }));
    }

    [Fact]
    public void Matches_FewerPinsThanParams_TrailingWildcards()
    {
        var m = Match("Boolean IsDisposition(Guid, Rank, Operator)", ParameterPin.Pin("Benevolent"));
        Assert.True(m.Matches("Boolean IsDisposition(Guid, Rank, Operator)", new[] { "Benevolent", "2", "GT" }));
    }

    [Fact]
    public void Matches_PinIndexBeyondParams_NoMatch_NoThrow()
    {
        var m = Match("Boolean IsDisposition(Guid, Rank, Operator)",
            ParameterPin.Wildcard, ParameterPin.Pin("2"));
        Assert.False(m.Matches("Boolean IsDisposition(Guid, Rank, Operator)", new[] { "Benevolent" }));
    }

    [Fact]
    public void Matches_ScriptCallAdapter()
    {
        var m = Match("Void SetGlobalValue(String, Int32)", ParameterPin.Pin("g"), ParameterPin.Wildcard);
        var call = new ScriptCall("Void SetGlobalValue(String, Int32)", new[] { "g", "1" }, ScriptCategory.Enter);
        Assert.True(m.Matches(call));
    }
}
