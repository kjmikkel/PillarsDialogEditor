using DialogEditor.Core.Parsing;
using Xunit;

namespace DialogEditor.Tests.Parsing;

public class GlobalVariablesCsvParserTests
{
    // Typical GlobalVariables.csv: first column is the variable name; header row present.
    private const string Fixture = """
        Name,DefaultValue,Type
        npc_met_eder,0,Int
        quest_accepted,0,Int
        player_gold,100,Int
        """;

    [Fact]
    public void Parse_ExtractsFirstColumn()
    {
        var entries = GlobalVariablesCsvParser.Parse(Fixture);
        Assert.Equal(3, entries.Count);
        Assert.Contains(entries, e => e.Name == "npc_met_eder");
        Assert.Contains(entries, e => e.Name == "quest_accepted");
        Assert.Contains(entries, e => e.Name == "player_gold");
    }

    [Fact]
    public void Parse_SkipsHeaderRow()
    {
        var entries = GlobalVariablesCsvParser.Parse(Fixture);
        Assert.DoesNotContain(entries, e => e.Name == "Name");
    }

    [Fact]
    public void Parse_IdIsEmpty_ForEveryEntry()
    {
        var entries = GlobalVariablesCsvParser.Parse(Fixture);
        Assert.All(entries, e => Assert.Empty(e.Id));
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
        => Assert.Empty(GlobalVariablesCsvParser.Parse(string.Empty));

    [Fact]
    public void Parse_HeaderOnly_ReturnsEmpty()
        => Assert.Empty(GlobalVariablesCsvParser.Parse("Name,DefaultValue,Type"));

    [Fact]
    public void ParseFile_NonExistentPath_ReturnsEmpty()
        => Assert.Empty(GlobalVariablesCsvParser.ParseFile(@"C:\does\not\exist.csv"));
}
