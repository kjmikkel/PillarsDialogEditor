using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

/// Covers the one-pass ParseAllByType sweep (grouping by short $type) used by the
/// generic lookup-kind sweep — see 2026-07-09-lookup-kind-sweep-design.md.
public class Poe2GameDataBundleParserSweepTests
{
    private const string MixedBundle = """
        {"GameDataObjects":[
          {"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
           "DebugName":"SHP_Defiant","ID":"11111111-1111-1111-1111-111111111111"},
          {"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
           "DebugName":"SHP_Dhow","ID":"22222222-2222-2222-2222-222222222222"},
          {"$type":"Game.GameData.ShipUpgradeGameData, Assembly-CSharp",
           "DebugName":"SHP_UP_Sails","ID":"33333333-3333-3333-3333-333333333333"},
          {"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
           "DebugName":"","ID":"44444444-4444-4444-4444-444444444444"},
          {"$type":"Game.GameData.ShipGameData, Assembly-CSharp",
           "DebugName":"NoId","ID":""}
        ]}
        """;

    [Fact]
    public void ParseAllByType_GroupsByShortType()
    {
        var byType = Poe2GameDataBundleParser.ParseAllByType(MixedBundle);
        Assert.Equal(2, byType["ShipGameData"].Count);
        Assert.Single(byType["ShipUpgradeGameData"]);
        Assert.Equal("SHP_Defiant", byType["ShipGameData"][0].Name);
    }

    [Fact]
    public void ParseAllByType_SkipsEmptyIdOrDebugName()
    {
        var byType = Poe2GameDataBundleParser.ParseAllByType(MixedBundle);
        Assert.DoesNotContain(byType["ShipGameData"], e => e.Name == "NoId");
        Assert.DoesNotContain(byType["ShipGameData"], e => e.Name.Length == 0);
    }

    [Fact]
    public void ParseAllByTypeFile_MissingFile_ReturnsEmpty()
        => Assert.Empty(Poe2GameDataBundleParser.ParseAllByTypeFile(
            Path.Combine(Path.GetTempPath(), "does-not-exist.gamedatabundle")));
}
