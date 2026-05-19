using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ConditionCatalogueTests
{
    private static readonly ConditionCatalogue Catalogue = ConditionCatalogue.LoadEmbedded();

    [Fact]
    public void LoadEmbedded_ReturnsNonEmptyList()
    {
        Assert.NotEmpty(Catalogue.All);
    }

    [Fact]
    public void Find_KnownMethod_ReturnsEntry()
    {
        var entry = Catalogue.Find("IsGlobalValue");
        Assert.NotNull(entry);
        Assert.Equal("IsGlobalValue", entry.MethodName);
        Assert.Equal("Is Global Value", entry.DisplayName);
    }

    [Fact]
    public void Find_UnknownMethod_ReturnsNull()
    {
        Assert.Null(Catalogue.Find("NonExistentCondition_XYZ"));
    }

    [Fact]
    public void ForGame_Poe1_IncludesGlobalsCondition()
    {
        var entries = Catalogue.ForGame("poe1");
        Assert.Contains(entries, e => e.MethodName == "IsGlobalValue");
    }

    [Fact]
    public void ForGame_Poe2_IncludesShipCondition()
    {
        var entries = Catalogue.ForGame("poe2");
        Assert.Contains(entries, e => e.MethodName == "IsPlayerShipAttribute");
    }

    [Fact]
    public void ForGame_Poe1_ExcludesPoE2OnlyCondition()
    {
        var entries = Catalogue.ForGame("poe1");
        Assert.DoesNotContain(entries, e => e.MethodName == "IsPlayerShipAttribute");
    }

    [Fact]
    public void Entry_HasParameters()
    {
        var entry = Catalogue.Find("IsGlobalValue");
        Assert.NotNull(entry);
        Assert.Equal(3, entry.Parameters.Count);
        Assert.Equal("Tag", entry.Parameters[0].Name);
    }

    [Fact]
    public void ForGame_CaseInsensitive()
    {
        Assert.Equal(Catalogue.ForGame("poe1").Count, Catalogue.ForGame("POE1").Count);
    }

    // ── FindByFullName ────────────────────────────────────────────────────

    [Fact]
    public void FindByFullName_ExactMatch_ReturnsEntry()
    {
        var entry = Catalogue.FindByFullName("Boolean IsGlobalValue(String, Operator, Int32)");
        Assert.NotNull(entry);
        Assert.Equal("IsGlobalValue", entry.MethodName);
    }

    [Fact]
    public void FindByFullName_UnknownFullName_ReturnsNull()
        => Assert.Null(Catalogue.FindByFullName("Boolean NonExistentCondition_XYZ(String)"));

    [Fact]
    public void FindByFullName_Poe1HasConversationNode_ReturnsPoe1Entry()
    {
        var entry = Catalogue.FindByFullName("Boolean HasConversationNodeBeenPlayed(String, Int32)");
        Assert.NotNull(entry);
        Assert.Contains("poe1", entry.Games);
        Assert.DoesNotContain("poe2", entry.Games);
    }

    [Fact]
    public void FindByFullName_Poe2HasConversationNode_ReturnsPoe2Entry()
    {
        var entry = Catalogue.FindByFullName("Boolean HasConversationNodeBeenPlayed(Guid, Int32)");
        Assert.NotNull(entry);
        Assert.Contains("poe2", entry.Games);
        Assert.DoesNotContain("poe1", entry.Games);
    }
}
