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
        Assert.Equal("Tag", entry.Parameters[0].Name);
    }

    [Fact]
    public void Entry_Label_ContainsDisplayNameAndCategory()
    {
        var entry = Catalogue.Find("SetGlobalValue")!;
        Assert.Contains(entry.DisplayName, entry.Label);
        Assert.Contains(entry.Category,    entry.Label);
    }
}
