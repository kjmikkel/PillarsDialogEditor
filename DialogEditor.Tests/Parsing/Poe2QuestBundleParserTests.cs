using DialogEditor.Core.Parsing;
using Xunit;

namespace DialogEditor.Tests.Parsing;

public class Poe2QuestBundleParserTests
{
    private const string Fixture = """
        {
          "Hash": -844961556,
          "Quests": [
            { "ID": "aaaaaaaa-0000-0000-0000-000000000001", "Filename": "Quests/Main/01_MainQuest.quest" },
            { "ID": "bbbbbbbb-0000-0000-0000-000000000002", "Filename": "Quests/Side/02_SideQuest.quest" }
          ]
        }
        """;

    [Fact]
    public void Parse_ExtractsIdAndFilenameBasename()
    {
        var entries = Poe2QuestBundleParser.Parse(Fixture);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e =>
            e.Id   == "aaaaaaaa-0000-0000-0000-000000000001" &&
            e.Name == "01_MainQuest");
    }

    [Fact]
    public void Parse_StripsPathAndExtension()
    {
        var entries = Poe2QuestBundleParser.Parse(Fixture);
        Assert.Contains(entries, e => e.Name == "02_SideQuest");
    }

    [Fact]
    public void Parse_EmptyQuests_ReturnsEmpty()
        => Assert.Empty(Poe2QuestBundleParser.Parse("""{"Hash":0,"Quests":[]}"""));

    [Fact]
    public void Parse_SkipsEntriesWithBlankIdOrFilename()
    {
        const string json = """
            {
              "Quests": [
                { "ID": "", "Filename": "Quests/X.quest" },
                { "ID": "cccccccc-0000-0000-0000-000000000003", "Filename": "" },
                { "ID": "dddddddd-0000-0000-0000-000000000004", "Filename": "Quests/Valid.quest" }
              ]
            }
            """;
        var entries = Poe2QuestBundleParser.Parse(json);
        Assert.Single(entries);
        Assert.Equal("Valid", entries[0].Name);
    }

    [Fact]
    public void ParseFile_NonExistentPath_ReturnsEmpty()
        => Assert.Empty(Poe2QuestBundleParser.ParseFile(@"C:\does\not\exist.questbundle"));
}
