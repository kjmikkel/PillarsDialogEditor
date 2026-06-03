using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class LanguageNameResolverTests
{
    public LanguageNameResolverTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void Resolve_KnownCode_ReturnsLocalizedNameKey()
    {
        Assert.Equal("Language_Name_fr", LanguageNameResolver.Resolve("fr"));
    }

    [Fact]
    public void Resolve_KnownCode_IsCaseInsensitive()
    {
        Assert.Equal("Language_Name_fr", LanguageNameResolver.Resolve("FR"));
    }

    [Fact]
    public void Resolve_HyphenatedKnownCode_MapsToSafeKey()
    {
        Assert.Equal("Language_Name_ptBR", LanguageNameResolver.Resolve("pt-BR"));
    }

    [Fact]
    public void Resolve_UnknownCode_ReturnsRawCode()
    {
        Assert.Equal("xx", LanguageNameResolver.Resolve("xx"));
    }
}
