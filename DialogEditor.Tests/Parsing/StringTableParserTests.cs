using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class StringTableParserTests
{
    private const string SampleXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <StringTableFile>
          <Entries>
            <Entry>
              <ID>1</ID>
              <DefaultText>Hello, traveller.</DefaultText>
              <FemaleText />
            </Entry>
            <Entry>
              <ID>2</ID>
              <DefaultText>Farewell.</DefaultText>
              <FemaleText>Farewell, sister.</FemaleText>
            </Entry>
          </Entries>
        </StringTableFile>
        """;

    [Fact]
    public void Parses_entry_count()
    {
        var table = StringTableParser.Parse(SampleXml);
        Assert.Equal(2, table.Count);
    }

    [Fact]
    public void Parses_default_text()
    {
        var table = StringTableParser.Parse(SampleXml);
        Assert.Equal("Hello, traveller.", table.Get(1)!.DefaultText);
    }

    [Fact]
    public void Parses_female_text()
    {
        var table = StringTableParser.Parse(SampleXml);
        Assert.Equal("Farewell, sister.", table.Get(2)!.FemaleText);
    }

    [Fact]
    public void Empty_female_text_stored_as_empty_string()
    {
        var table = StringTableParser.Parse(SampleXml);
        Assert.Equal(string.Empty, table.Get(1)!.FemaleText);
    }

    [Fact]
    public void Missing_id_returns_null()
    {
        var table = StringTableParser.Parse(SampleXml);
        Assert.Null(table.Get(99));
    }
}
