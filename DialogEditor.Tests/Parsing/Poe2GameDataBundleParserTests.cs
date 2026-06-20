using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using Xunit;

namespace DialogEditor.Tests.Parsing;

public class Poe2GameDataBundleParserTests
{
    private const string SpeakerFixture = """
        {
          "GameDataObjects": [
            { "ID": "aaaaaaaa-0000-0000-0000-000000000001", "DebugName": "SPK_Companion_Eder" },
            { "ID": "bbbbbbbb-0000-0000-0000-000000000002", "DebugName": "SPK_NPC_Innkeeper" }
          ]
        }
        """;

    private const string QuestFixture = """
        {
          "GameDataObjects": [
            { "ID": "cccccccc-0000-0000-0000-000000000003", "DebugName": "Q01_MainQuest" },
            { "ID": "dddddddd-0000-0000-0000-000000000004", "DebugName": "Q02_SideQuest" }
          ]
        }
        """;

    [Fact]
    public void Parse_ExtractsIdAndDebugName()
    {
        var entries = Poe2GameDataBundleParser.Parse(QuestFixture);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Id == "cccccccc-0000-0000-0000-000000000003");
    }

    [Fact]
    public void Parse_AppliesCleanName()
    {
        var entries = Poe2GameDataBundleParser.Parse(
            SpeakerFixture,
            name => name.Replace("SPK_Companion_", "").Replace("SPK_NPC_", ""));
        Assert.Contains(entries, e => e.Name == "Eder");
        Assert.Contains(entries, e => e.Name == "Innkeeper");
    }

    [Fact]
    public void Parse_WithoutCleanName_UsesDebugNameAsIs()
    {
        var entries = Poe2GameDataBundleParser.Parse(QuestFixture);
        Assert.Contains(entries, e => e.Name == "Q01_MainQuest");
    }

    [Fact]
    public void Parse_EmptyObjects_ReturnsEmpty()
    {
        var entries = Poe2GameDataBundleParser.Parse("""{"GameDataObjects":[]}""");
        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_SkipsEntriesWithBlankIdOrName()
    {
        const string json = """
            {
              "GameDataObjects": [
                { "ID": "", "DebugName": "HasNoId" },
                { "ID": "eeeeeeee-0000-0000-0000-000000000005", "DebugName": "" },
                { "ID": "ffffffff-0000-0000-0000-000000000006", "DebugName": "Valid" }
              ]
            }
            """;
        var entries = Poe2GameDataBundleParser.Parse(json);
        Assert.Single(entries);
        Assert.Equal("Valid", entries[0].Name);
    }

    [Fact]
    public void Parse_WorksForDifferentKindFixtures()
    {
        // Confirms the parser is generic — same code handles Quests and Speakers
        var speakers = Poe2GameDataBundleParser.Parse(SpeakerFixture);
        var quests   = Poe2GameDataBundleParser.Parse(QuestFixture);
        Assert.Equal(2, speakers.Count);
        Assert.Equal(2, quests.Count);
    }

    [Fact]
    public void ParseFile_NonExistentPath_ReturnsEmpty()
        => Assert.Empty(Poe2GameDataBundleParser.ParseFile(@"C:\does\not\exist.gamedatabundle"));

    // ── typeFilter (characters.gamedatabundle multi-kind support) ──────────

    private const string MixedTypeFixture = """
        {
          "GameDataObjects": [
            { "$type": "Game.GameData.RaceGameData, Assembly-CSharp", "ID": "race-0001", "DebugName": "Human" },
            { "$type": "Game.GameData.BaseStatsGameData, Assembly-CSharp", "ID": "class-0001", "DebugName": "Paladin" },
            { "$type": "Game.GameData.PaladinSubClassGameData, Assembly-CSharp", "ID": "order-0001", "DebugName": "Paladin_DarcozziPaladini" }
          ]
        }
        """;

    [Fact]
    public void Parse_TypeFilter_Null_ReturnsAll()
    {
        var entries = Poe2GameDataBundleParser.Parse(MixedTypeFixture);
        Assert.Equal(3, entries.Count);
    }

    [Fact]
    public void Parse_TypeFilter_ReturnsOnlyMatchingType()
    {
        var races = Poe2GameDataBundleParser.Parse(MixedTypeFixture, typeFilter: "RaceGameData");
        Assert.Single(races);
        Assert.Equal("Human", races[0].Name);
    }

    [Fact]
    public void Parse_TypeFilter_IsCaseInsensitive()
    {
        var races = Poe2GameDataBundleParser.Parse(MixedTypeFixture, typeFilter: "racegamedata");
        Assert.Single(races);
    }

    [Fact]
    public void Parse_TypeFilter_NoMatch_ReturnsEmpty()
    {
        var entries = Poe2GameDataBundleParser.Parse(MixedTypeFixture, typeFilter: "NonExistentType");
        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_TypeFilter_WithCleanName_BothApplied()
    {
        Func<string, string> stripPrefix = n =>
            n.StartsWith("Paladin_", StringComparison.OrdinalIgnoreCase) ? n[8..] : n;

        var orders = Poe2GameDataBundleParser.Parse(
            MixedTypeFixture,
            cleanName:  stripPrefix,
            typeFilter: "PaladinSubClassGameData");

        Assert.Single(orders);
        Assert.Equal("DarcozziPaladini", orders[0].Name);
        Assert.Equal("order-0001", orders[0].Id);
    }

    [Fact]
    public void Parse_TypeFilter_EntriesWithoutTypeField_IncludedWhenFilterIsNull()
    {
        // Existing fixtures have no $type — must still parse without typeFilter.
        var entries = Poe2GameDataBundleParser.Parse(QuestFixture, typeFilter: null);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Parse_TypeFilter_EntriesWithoutTypeField_ExcludedWhenFilterSet()
    {
        // Objects with no $type field don't match any typeFilter.
        var entries = Poe2GameDataBundleParser.Parse(QuestFixture, typeFilter: "Quest");
        Assert.Empty(entries);
    }

    [Fact]
    public void Parse_CleanNameProducesEmptyString_EntryIsDropped()
    {
        // If a cleanName transform returns "" (e.g. DispositionGameData entry whose DebugName IS
        // exactly "Disposition"), the resulting entry has no displayable name and must be dropped.
        // Allowing it through produces " — <guid>" suggestions that crash the AutoCompleteBox.
        const string json = """
            {
              "GameDataObjects": [
                { "ID": "aaa00001-0000-0000-0000-000000000000", "DebugName": "Disposition" },
                { "ID": "aaa00002-0000-0000-0000-000000000000", "DebugName": "HostileDisposition" }
              ]
            }
            """;
        Func<string, string> stripSuffix = n =>
            n.EndsWith("Disposition", StringComparison.Ordinal) ? n[..^"Disposition".Length].TrimEnd() : n;

        var entries = Poe2GameDataBundleParser.Parse(json, cleanName: stripSuffix);

        // "Disposition" → "" → dropped
        // "HostileDisposition" → "Hostile" → kept
        Assert.Single(entries);
        Assert.Equal("Hostile", entries[0].Name);
    }

    // ── componentFilter (BaseStatsGameData IsPlayerClass support) ──────────

    private const string PlayerClassFixture = """
        {
          "GameDataObjects": [
            {
              "$type": "Game.GameData.BaseStatsGameData, Assembly-CSharp",
              "ID": "player-class-01",
              "DebugName": "Paladin",
              "Components": [
                {"$type": "Game.GameData.BaseStatsComponent, Assembly-CSharp", "IsPlayerClass": "true"}
              ]
            },
            {
              "$type": "Game.GameData.BaseStatsGameData, Assembly-CSharp",
              "ID": "npc-creature-01",
              "DebugName": "AdraDragon",
              "Components": [
                {"$type": "Game.GameData.BaseStatsComponent, Assembly-CSharp", "IsPlayerClass": "false"}
              ]
            }
          ]
        }
        """;

    [Fact]
    public void Parse_ComponentFilter_Null_ReturnsAllMatchingTypeFilter()
    {
        var entries = Poe2GameDataBundleParser.Parse(PlayerClassFixture, typeFilter: "BaseStatsGameData");
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void Parse_ComponentFilter_ExcludesEntriesWherePredicateReturnsFalse()
    {
        var entries = Poe2GameDataBundleParser.Parse(PlayerClassFixture,
            typeFilter: "BaseStatsGameData",
            componentFilter: comps => comps.Any(c =>
                c.TryGetProperty("IsPlayerClass", out var p) &&
                p.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true));

        Assert.Single(entries);
        Assert.Equal("Paladin", entries[0].Name);
        Assert.Equal("player-class-01", entries[0].Id);
    }

    [Fact]
    public void Parse_ComponentFilter_EntryWithNoComponents_IsExcluded()
    {
        const string json = """
            {
              "GameDataObjects": [
                {
                  "$type": "Game.GameData.BaseStatsGameData, Assembly-CSharp",
                  "ID": "no-components-01",
                  "DebugName": "NoComponents"
                }
              ]
            }
            """;

        var entries = Poe2GameDataBundleParser.Parse(json,
            typeFilter: "BaseStatsGameData",
            componentFilter: comps => comps.Any(c =>
                c.TryGetProperty("IsPlayerClass", out var p) &&
                p.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true));

        Assert.Empty(entries);
    }
}
