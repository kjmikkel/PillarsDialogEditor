using DialogEditor.Core.Parsing;

namespace DialogEditor.Tests.Parsing;

public class Poe1SpeakerNameParserTests
{
    private static string MakeConversation(params (string guid, string tag)[] mappings)
    {
        var entries = string.Join("", mappings.Select(m =>
            $"<CharacterMapping><Guid>{m.guid}</Guid><InstanceTag>{m.tag}</InstanceTag></CharacterMapping>"));
        return $"<ConversationData><Nodes/><CharacterMappings>{entries}</CharacterMappings></ConversationData>";
    }

    private static string MakeStringtable(params string[] names)
    {
        var entries = string.Join("", names.Select((n, i) =>
            $"<Entry><ID>{i + 1}</ID><DefaultText>{n}</DefaultText><FemaleText/></Entry>"));
        return $"<StringTableFile><Entries>{entries}</Entries></StringTableFile>";
    }

    private static readonly string EmptyStringtable = MakeStringtable();

    [Fact]
    public void Parse_NoConversations_ReturnsEmptyDict()
    {
        var result = Poe1SpeakerNameParser.Parse([], EmptyStringtable);
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NpcPrefix_ExactMatch_ResolvesName()
    {
        var conv = MakeConversation(("90929d34-b900-47aa-826d-0f465c4cac97", "NPC_Pace"));
        var st = MakeStringtable("Pace", "Aloth");
        var result = Poe1SpeakerNameParser.Parse([conv], st);
        Assert.Equal("Pace", result["90929d34-b900-47aa-826d-0f465c4cac97"]);
    }

    [Fact]
    public void Parse_NpcPrefix_AllCapsTag_ResolvesMatchingCase()
    {
        var conv = MakeConversation(("ec00b40d-036a-4b47-bc52-71d23ff463db", "NPC_CELBY"));
        var st = MakeStringtable("Celby");
        var result = Poe1SpeakerNameParser.Parse([conv], st);
        Assert.Equal("Celby", result["ec00b40d-036a-4b47-bc52-71d23ff463db"]);
    }

    [Fact]
    public void Parse_CompanionPrefix_ResolvesName()
    {
        var conv = MakeConversation(("aloth-guid-0000-0000-0000-000000000000", "Companion_Aloth"));
        var st = MakeStringtable("Aloth", "Edér");
        var result = Poe1SpeakerNameParser.Parse([conv], st);
        Assert.Equal("Aloth", result["aloth-guid-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_Px1NpcPrefix_StripsBothPrefixes()
    {
        var conv = MakeConversation(("bbbbbbbb-0000-0000-0000-000000000000", "PX1_NPC_Concelhaut"));
        var st = MakeStringtable("Concelhaut", "Aloth");
        var result = Poe1SpeakerNameParser.Parse([conv], st);
        Assert.Equal("Concelhaut", result["bbbbbbbb-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_Px1CrePrefix_StripsBothPrefixes()
    {
        var conv = MakeConversation(("cccccccc-0000-0000-0000-000000000000", "PX1_CRE_Lich_Concelhaut"));
        var st = MakeStringtable("Lich Concelhaut");
        var result = Poe1SpeakerNameParser.Parse([conv], st);
        Assert.Equal("Lich Concelhaut", result["cccccccc-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_NoStringtableMatch_ReturnsFormattedInstanceTag()
    {
        var conv = MakeConversation(("dddddddd-0000-0000-0000-000000000000", "NPC_Skaen_Priest_02"));
        var result = Poe1SpeakerNameParser.Parse([conv], EmptyStringtable);
        Assert.Equal("Skaen Priest 02", result["dddddddd-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_NoPrefix_ReturnsTagAsIs()
    {
        var conv = MakeConversation(("eeeeeeee-0000-0000-0000-000000000000", "Defiance Bay AMBIENT"));
        var result = Poe1SpeakerNameParser.Parse([conv], EmptyStringtable);
        Assert.Equal("Defiance Bay AMBIENT", result["eeeeeeee-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_DuplicateGuid_FirstSeenWins()
    {
        var conv1 = MakeConversation(("aaaabbbb-0000-0000-0000-000000000000", "NPC_Pace"));
        var conv2 = MakeConversation(("aaaabbbb-0000-0000-0000-000000000000", "NPC_Aldhem"));
        var st = MakeStringtable("Pace", "Aldhelm");
        var result = Poe1SpeakerNameParser.Parse([conv1, conv2], st);
        Assert.Equal("Pace", result["aaaabbbb-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_MultipleConversations_AllGuidsCaptured()
    {
        var conv1 = MakeConversation(("11111111-0000-0000-0000-000000000000", "NPC_Pace"));
        var conv2 = MakeConversation(("22222222-0000-0000-0000-000000000000", "Companion_Aloth"));
        var st = MakeStringtable("Pace", "Aloth");
        var result = Poe1SpeakerNameParser.Parse([conv1, conv2], st);
        Assert.Equal(2, result.Count);
        Assert.Equal("Pace", result["11111111-0000-0000-0000-000000000000"]);
        Assert.Equal("Aloth", result["22222222-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_LookupIsCaseInsensitive()
    {
        var conv = MakeConversation(("EC00B40D-036A-4B47-BC52-71D23FF463DB", "NPC_CELBY"));
        var st = MakeStringtable("Celby");
        var result = Poe1SpeakerNameParser.Parse([conv], st);
        Assert.True(result.ContainsKey("ec00b40d-036a-4b47-bc52-71d23ff463db"));
    }

    [Fact]
    public void Parse_TrailingNumberStripped_ForLookupOnly()
    {
        // NPC_Audience_Member_3 → strip number for lookup → "Audience Member" → no match → "Audience Member 3"
        var conv = MakeConversation(("ffffffff-0000-0000-0000-000000000000", "NPC_Audience_Member_3"));
        var result = Poe1SpeakerNameParser.Parse([conv], EmptyStringtable);
        Assert.Equal("Audience Member 3", result["ffffffff-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_TrailingNumberStripped_MatchesBaseNameIfPresent()
    {
        // If "Audience Member" is in the stringtable, the tag with trailing number resolves to it
        var conv = MakeConversation(("ffffffff-0000-0000-0000-000000000000", "NPC_Audience_Member_3"));
        var st = MakeStringtable("Audience Member");
        var result = Poe1SpeakerNameParser.Parse([conv], st);
        Assert.Equal("Audience Member", result["ffffffff-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_GGPCodename_ResolvesDurance()
    {
        var conv = MakeConversation(("durance-guid-0000-0000-0000-000000000000", "Companion_GGP"));
        var result = Poe1SpeakerNameParser.Parse([conv], EmptyStringtable);
        Assert.Equal("Durance", result["durance-guid-0000-0000-0000-000000000000"]);
    }

    [Fact]
    public void Parse_GMCodename_ResolvesGrievingMother()
    {
        var conv = MakeConversation(("gm-guid-0000-0000-0000-000000000000", "Companion_GM"));
        var result = Poe1SpeakerNameParser.Parse([conv], EmptyStringtable);
        Assert.Equal("Grieving Mother", result["gm-guid-0000-0000-0000-000000000000"]);
    }
}
