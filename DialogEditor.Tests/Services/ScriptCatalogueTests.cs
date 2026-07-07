using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ScriptCatalogueTests
{
    private static readonly ScriptCatalogue Catalogue = ScriptCatalogue.LoadEmbedded();

    [Fact]
    public void LoadEmbedded_ReturnsNonEmptyList()
        => Assert.NotEmpty(Catalogue.All);

    [Fact]
    public void Find_SetGlobalValue_ReturnsEntry()
    {
        var entry = Catalogue.Find("SetGlobalValue");
        Assert.NotNull(entry);
        Assert.Equal("SetGlobalValue", entry.MethodName);
    }

    [Fact]
    public void Find_UnknownMethod_ReturnsNull()
        => Assert.Null(Catalogue.Find("NonExistentScript_XYZ"));

    [Fact]
    public void ForGame_Poe1_IncludesSetGlobal()
        => Assert.Contains(Catalogue.ForGame("poe1"), e => e.MethodName == "SetGlobalValue");

    [Fact]
    public void ForGame_CaseInsensitive()
        => Assert.Equal(Catalogue.ForGame("poe1").Count, Catalogue.ForGame("POE1").Count);

    [Fact]
    public void Entry_ReflectionFullName_StartsWithVoid()
    {
        var entry = Catalogue.Find("SetGlobalValue")!;
        Assert.StartsWith("Void ", entry.ReflectionFullName);
    }

    [Fact]
    public void Entry_HasParameters()
    {
        var entry = Catalogue.Find("SetGlobalValue")!;
        Assert.NotEmpty(entry.Parameters);
        Assert.Equal("Name", entry.Parameters[0].Name);
    }

    [Fact]
    public void Entry_Label_ContainsDisplayNameAndCategory()
    {
        var entry = Catalogue.Find("SetGlobalValue")!;
        Assert.Contains(entry.DisplayName, entry.Label);
        Assert.Contains(entry.Category,    entry.Label);
    }

    // ── Game differentiation ──────────────────────────────────────────────

    [Fact]
    public void ForGame_Poe1_IncludesStringQuestScript()
    {
        // PoE1 StartQuest takes a string name
        var entries = Catalogue.ForGame("poe1");
        Assert.Contains(entries, e => e.MethodName == "StartQuest"
                                   && e.ReflectionFullName.Contains("String"));
    }

    [Fact]
    public void ForGame_Poe2_IncludesGuidQuestScript()
    {
        // PoE2 StartQuest takes a Guid
        var entries = Catalogue.ForGame("poe2");
        Assert.Contains(entries, e => e.MethodName == "StartQuest"
                                   && e.ReflectionFullName.Contains("Guid"));
    }

    [Fact]
    public void ForGame_Poe1_ExcludesGuidQuestScript()
    {
        var entries = Catalogue.ForGame("poe1");
        Assert.DoesNotContain(entries, e => e.MethodName == "StartQuest"
                                         && e.ReflectionFullName.Contains("Guid"));
    }

    [Fact]
    public void ForGame_Poe2_ExcludesStringQuestScript()
    {
        var entries = Catalogue.ForGame("poe2");
        Assert.DoesNotContain(entries, e => e.MethodName == "StartQuest"
                                         && e.ReflectionFullName.Contains("String"));
    }

    // ── FindByFullName ────────────────────────────────────────────────────

    [Fact]
    public void FindByFullName_ExactMatch_ReturnsEntry()
    {
        var entry = Catalogue.FindByFullName("Void SetGlobalValue(String, Int32)");
        Assert.NotNull(entry);
        Assert.Equal("SetGlobalValue", entry.MethodName);
    }

    [Fact]
    public void FindByFullName_UnknownFullName_ReturnsNull()
        => Assert.Null(Catalogue.FindByFullName("Void NonExistentScript(String)"));

    [Fact]
    public void FindByFullName_Poe1StartQuest_ReturnsPoe1Entry()
    {
        var entry = Catalogue.FindByFullName("Void StartQuest(String)");
        Assert.NotNull(entry);
        Assert.Contains("poe1", entry.Games);
        Assert.DoesNotContain("poe2", entry.Games);
    }

    [Fact]
    public void FindByFullName_Poe2StartQuest_ReturnsPoe2Entry()
    {
        var entry = Catalogue.FindByFullName("Void StartQuest(Guid)");
        Assert.NotNull(entry);
        Assert.Contains("poe2", entry.Games);
        Assert.DoesNotContain("poe1", entry.Games);
    }
}
