using DialogEditor.Core.Parsing;
using Xunit;

namespace DialogEditor.Tests.Parsing;

public class Poe1GlobalVariablesParserTests
{
    private const string TwoVarXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <GlobalVariablesData>
          <Folders />
          <GlobalVariables>
            <GlobalVariable>
              <Tag>bBanterDisabled</Tag>
              <FolderGuid>00000000-0000-0000-0000-000000000000</FolderGuid>
              <InitialValue>0</InitialValue>
              <Comments>Disable banter</Comments>
              <CreatedBy />
            </GlobalVariable>
            <GlobalVariable>
              <Tag>npc_met_eder</Tag>
              <FolderGuid>00000000-0000-0000-0000-000000000000</FolderGuid>
              <InitialValue>0</InitialValue>
              <Comments />
              <CreatedBy />
            </GlobalVariable>
          </GlobalVariables>
        </GlobalVariablesData>
        """;

    [Fact]
    public void Parse_ExtractsTagAsName()
    {
        var entries = Poe1GlobalVariablesParser.Parse(TwoVarXml);
        Assert.Equal(2, entries.Count);
        Assert.Contains(entries, e => e.Name == "bBanterDisabled");
        Assert.Contains(entries, e => e.Name == "npc_met_eder");
    }

    [Fact]
    public void Parse_IdIsEmpty_ForEveryEntry()
    {
        var entries = Poe1GlobalVariablesParser.Parse(TwoVarXml);
        Assert.All(entries, e => Assert.Empty(e.Id));
    }

    [Fact]
    public void Parse_SkipsEntriesWithEmptyTag()
    {
        const string xml = """
            <GlobalVariablesData>
              <GlobalVariables>
                <GlobalVariable><Tag></Tag></GlobalVariable>
                <GlobalVariable><Tag>valid_var</Tag></GlobalVariable>
              </GlobalVariables>
            </GlobalVariablesData>
            """;

        var entries = Poe1GlobalVariablesParser.Parse(xml);

        Assert.Single(entries);
        Assert.Equal("valid_var", entries[0].Name);
    }

    [Fact]
    public void Parse_EmptyDocument_ReturnsEmpty()
    {
        var entries = Poe1GlobalVariablesParser.Parse(
            "<GlobalVariablesData><GlobalVariables /></GlobalVariablesData>");
        Assert.Empty(entries);
    }

    [Fact]
    public void ParseFile_NonExistentPath_ReturnsEmpty()
        => Assert.Empty(Poe1GlobalVariablesParser.ParseFile(@"C:\does\not\exist.globalvariables"));

    // Issue 345: the Game State panel can start from the game's own start-of-game values.
    [Fact]
    public void ParseInitialValues_MapsEachTagToItsInitialValue()
    {
        const string xml = """
            <GlobalVariablesData>
              <GlobalVariables>
                <GlobalVariable><Tag>n_Gold_Start</Tag><InitialValue>10</InitialValue></GlobalVariable>
                <GlobalVariable><Tag>n_Unset</Tag><InitialValue>-1</InitialValue></GlobalVariable>
                <GlobalVariable><Tag>b_Plain</Tag><InitialValue>0</InitialValue></GlobalVariable>
              </GlobalVariables>
            </GlobalVariablesData>
            """;

        var values = Poe1GlobalVariablesParser.ParseInitialValues(xml);

        Assert.Equal(3, values.Count);
        Assert.Equal(10, values["n_Gold_Start"]);
        Assert.Equal(-1, values["n_Unset"]);
        Assert.Equal(0, values["b_Plain"]);
    }

    [Fact]
    public void ParseInitialValues_MissingOrUnreadableValue_IsZero_AndEmptyTagsAreSkipped()
    {
        const string xml = """
            <GlobalVariablesData>
              <GlobalVariables>
                <GlobalVariable><Tag>no_value</Tag></GlobalVariable>
                <GlobalVariable><Tag>junk_value</Tag><InitialValue>abc</InitialValue></GlobalVariable>
                <GlobalVariable><Tag></Tag><InitialValue>4</InitialValue></GlobalVariable>
              </GlobalVariables>
            </GlobalVariablesData>
            """;

        var values = Poe1GlobalVariablesParser.ParseInitialValues(xml);

        Assert.Equal(2, values.Count);
        Assert.Equal(0, values["no_value"]);
        Assert.Equal(0, values["junk_value"]);
    }

    [Fact]
    public void ParseInitialValuesFile_NonExistentPath_ReturnsEmpty()
        => Assert.Empty(Poe1GlobalVariablesParser.ParseInitialValuesFile(@"C:\does\not\exist.globalvariables"));
}
