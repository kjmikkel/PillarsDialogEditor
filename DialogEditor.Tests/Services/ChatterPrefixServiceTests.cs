using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ChatterPrefixServiceTests : IDisposable
{
    public void Dispose() => ChatterPrefixService.Clear();

    [Fact]
    public void GetPrefix_UnknownGuid_ReturnsNull()
    {
        ChatterPrefixService.Clear();
        Assert.Null(ChatterPrefixService.GetPrefix("00000000-0000-0000-0000-000000000000"));
    }

    [Fact]
    public void Register_ThenGetPrefix_ReturnsPrefix()
    {
        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder" }
        });
        Assert.Equal("eder", ChatterPrefixService.GetPrefix("9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
    }

    [Fact]
    public void Register_LookupIsCaseInsensitive()
    {
        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9C5F12C9-E93D-4952-9F1A-726C9498F8FB", "eder" }
        });
        Assert.Equal("eder", ChatterPrefixService.GetPrefix("9c5f12c9-e93d-4952-9f1a-726c9498f8fb"));
    }

    [Fact]
    public void Register_Twice_ReplacesData()
    {
        ChatterPrefixService.Register(new Dictionary<string, string> { { "aaa", "old" } });
        ChatterPrefixService.Register(new Dictionary<string, string> { { "bbb", "new" } });
        Assert.Null(ChatterPrefixService.GetPrefix("aaa"));
        Assert.Equal("new", ChatterPrefixService.GetPrefix("bbb"));
    }

    [Fact]
    public void Clear_RemovesAllData()
    {
        ChatterPrefixService.Register(new Dictionary<string, string> { { "aaa", "eder" } });
        ChatterPrefixService.Clear();
        Assert.Null(ChatterPrefixService.GetPrefix("aaa"));
    }
}
