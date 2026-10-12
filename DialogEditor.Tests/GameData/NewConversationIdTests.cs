using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.GameData;

/// Issue 162: a new conversation's ID is derived from its name, so every install of it
/// (the patcher rebuilds new conversations from scratch each time) gets the same ID, and
/// save games that refer to it keep working.
public class NewConversationIdTests
{
    [Fact]
    public void SameName_GivesTheSameId() =>
        Assert.Equal(NewConversationId.For("brand_new"), NewConversationId.For("brand_new"));

    // The game looks conversations up by name case-insensitively.
    [Fact]
    public void NamesDifferingOnlyInCase_GiveTheSameId() =>
        Assert.Equal(NewConversationId.For("brand_new"), NewConversationId.For("Brand_New"));

    [Fact]
    public void DifferentNames_GiveDifferentIds() =>
        Assert.NotEqual(NewConversationId.For("brand_new"), NewConversationId.For("brand_old"));

    // RFC 9562: version nibble 5, variant bits 10xx.
    [Fact]
    public void Id_IsAVersion5Uuid()
    {
        var text = NewConversationId.For("brand_new").ToString();
        Assert.Equal('5', text[14]);
        Assert.Contains(text[19], "89ab");
    }

    // Golden values, computed independently with Python's uuid.uuid5 under the editor's
    // namespace. Changing the namespace or the hashing would give every installed new
    // conversation a new ID and break save games that refer to it: that must be deliberate.
    [Theory]
    [InlineData("canonical_mod_new", "78c0ffb3-f8c9-5236-97eb-301ae5903c52")]
    [InlineData("brand_new",         "ab4251f2-bc23-5ddd-921a-af77b515c464")]
    public void Id_IsStableAcrossReleases(string name, string expected) =>
        Assert.Equal(Guid.Parse(expected), NewConversationId.For(name));
}
