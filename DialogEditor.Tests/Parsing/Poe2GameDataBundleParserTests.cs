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
}
