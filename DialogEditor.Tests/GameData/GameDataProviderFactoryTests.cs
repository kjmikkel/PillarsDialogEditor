using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.GameData;

public class GameDataProviderFactoryTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    public void Dispose() => Directory.Delete(_tempRoot, recursive: true);

    [Fact]
    public void Detect_Poe1DataFolder_ReturnsPoe1Provider()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "PillarsOfEternity_Data"));
        var provider = GameDataProviderFactory.Detect(_tempRoot);
        Assert.NotNull(provider);
        Assert.Equal("Pillars of Eternity", provider.GameName);
    }

    [Fact]
    public void Detect_Poe2DataFolder_ReturnsPoe2Provider()
    {
        Directory.CreateDirectory(Path.Combine(_tempRoot, "PillarsOfEternityII_Data"));
        var provider = GameDataProviderFactory.Detect(_tempRoot);
        Assert.NotNull(provider);
        Assert.Equal("Pillars of Eternity II: Deadfire", provider.GameName);
    }

    [Fact]
    public void Detect_UnrecognisedFolder_ReturnsNull()
    {
        Directory.CreateDirectory(_tempRoot);
        var provider = GameDataProviderFactory.Detect(_tempRoot);
        Assert.Null(provider);
    }
}
