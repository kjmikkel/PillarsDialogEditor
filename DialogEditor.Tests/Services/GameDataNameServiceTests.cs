using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class GameDataNameServiceTests : IDisposable
{
    public void Dispose() => GameDataNameService.Clear();

    [Fact]
    public void Get_UnregisteredKind_ReturnsEmpty()
        => Assert.Empty(GameDataNameService.Get("Quest"));

    [Fact]
    public void Register_ThenGet_ReturnsEntries()
    {
        var entries = new[] { new NamedEntry("My Quest — abc", "abc") };
        GameDataNameService.Register("Quest", entries);
        Assert.Equal(entries, GameDataNameService.Get("Quest"));
    }

    [Fact]
    public void Register_Twice_ReplacesEntries()
    {
        GameDataNameService.Register("Quest", [new NamedEntry("Old", "old")]);
        GameDataNameService.Register("Quest", [new NamedEntry("New", "new")]);
        Assert.Equal("New", GameDataNameService.Get("Quest")[0].DisplayName);
        Assert.Single(GameDataNameService.Get("Quest"));
    }

    [Fact]
    public void Clear_RemovesAllEntries()
    {
        GameDataNameService.Register("Quest", [new NamedEntry("Q", "q")]);
        GameDataNameService.Clear();
        Assert.Empty(GameDataNameService.Get("Quest"));
    }

    [Fact]
    public void Get_DifferentKinds_AreIndependent()
    {
        GameDataNameService.Register("Quest", [new NamedEntry("Q", "q")]);
        GameDataNameService.Register("Item",  [new NamedEntry("I", "i")]);
        Assert.Single(GameDataNameService.Get("Quest"));
        Assert.Single(GameDataNameService.Get("Item"));
    }
}
