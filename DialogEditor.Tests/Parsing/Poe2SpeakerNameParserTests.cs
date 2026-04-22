using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class Poe2SpeakerNameParserTests
{
    private static string MakeJson(params (string id, string debugName)[] entries)
    {
        var objects = string.Join(",", entries.Select(e =>
            $$$"""{"ID":"{{{e.id}}}","DebugName":"{{{e.debugName}}}"}"""));
        return $$$"""{"GameDataObjects":[{{{objects}}}]}""";
    }

    [Fact]
    public void Parse_EmptyBundle_ReturnsEmptyDict()
    {
        var result = Poe2SpeakerNameParser.Parse("""{"GameDataObjects":[]}""");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_PlainName_UsedDirectly()
    {
        var json = MakeJson(("b1a8e901-0000-0000-0000-000000000000", "Player"));
        var result = Poe2SpeakerNameParser.Parse(json);
        Assert.Equal("Player", result["b1a8e901-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_SpkCompanionPrefix_StripsToCharacterName()
    {
        var json = MakeJson(("5529e4b7-42dc-4895-b9f8-23375a945413", "SPK_Companion_Aloth"));
        var result = Poe2SpeakerNameParser.Parse(json);
        Assert.Equal("Aloth", result["5529e4b7-42dc-4895-b9f8-23375a945413"]);
    }

    [Fact]
    public void Parse_SpkNarratorEntry_ReturnsNarrator()
    {
        var json = MakeJson(("6a99a109-0000-0000-0000-000000000000", "SPK_Narrator"));
        var result = Poe2SpeakerNameParser.Parse(json);
        Assert.Equal("Narrator", result["6a99a109-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_SpkPlayerEntry_ReturnsPlayer()
    {
        var json = MakeJson(("834a2224-fb79-4f68-b266-28b1486d2701", "SPK_Player"));
        var result = Poe2SpeakerNameParser.Parse(json);
        Assert.Equal("Player", result["834a2224-fb79-4f68-b266-28b1486d2701"]);
    }

    [Fact]
    public void Parse_SpkNpcPrefix_StripsToCharacterName()
    {
        var json = MakeJson(("aaaaaaaa-0000-0000-0000-000000000000", "SPK_NPC_Harond"));
        var result = Poe2SpeakerNameParser.Parse(json);
        Assert.Equal("Harond", result["aaaaaaaa-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_SpkProPrefix_StripsToCharacterName()
    {
        var json = MakeJson(("bbbbbbbb-0000-0000-0000-000000000000", "SPK_PRO_Crewman"));
        var result = Poe2SpeakerNameParser.Parse(json);
        Assert.Equal("Crewman", result["bbbbbbbb-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_SpkAreaCode_StripsSpkPrefix()
    {
        var json = MakeJson(("cccccccc-0000-0000-0000-000000000000", "SPK_VD_Icheral"));
        var result = Poe2SpeakerNameParser.Parse(json);
        Assert.Equal("VD Icheral", result["cccccccc-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_MultipleEntries_AllPresent()
    {
        var json = MakeJson(
            ("5529e4b7-42dc-4895-b9f8-23375a945413", "SPK_Companion_Aloth"),
            ("9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "SPK_Companion_Eder")
        );
        var result = Poe2SpeakerNameParser.Parse(json);
        Assert.Equal(2, result.Count);
        Assert.Equal("Aloth", result["5529e4b7-42dc-4895-b9f8-23375a945413"]);
        Assert.Equal("Eder", result["9c5f12c9-e93d-4952-9f1a-726c9498f8fb"]);
    }

    [Fact]
    public void Parse_LookupIsCaseInsensitive()
    {
        var json = MakeJson(("5529E4B7-42DC-4895-B9F8-23375A945413", "SPK_Companion_Aloth"));
        var result = Poe2SpeakerNameParser.Parse(json);
        Assert.True(result.ContainsKey("5529e4b7-42dc-4895-b9f8-23375a945413"));
    }
}
