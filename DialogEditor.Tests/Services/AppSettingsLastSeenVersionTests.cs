using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class AppSettingsLastSeenVersionTests : IDisposable
{
    public AppSettingsLastSeenVersionTests()
        => AppSettings.SettingsPathOverride = Path.GetTempFileName();

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null && File.Exists(path)) File.Delete(path);
    }

    [Fact]
    public void LastSeenVersion_DefaultsToEmpty()
        => Assert.Equal("", AppSettings.LastSeenVersion);

    [Fact]
    public void LastSeenVersion_RoundTrips()
    {
        AppSettings.LastSeenVersion = "1.2.3";
        Assert.Equal("1.2.3", AppSettings.LastSeenVersion);
    }
}
