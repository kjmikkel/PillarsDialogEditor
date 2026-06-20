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
}
