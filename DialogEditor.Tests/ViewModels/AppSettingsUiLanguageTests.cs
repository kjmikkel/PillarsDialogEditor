using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class AppSettingsUiLanguageTests : IDisposable
{
    private readonly string _path;

    public AppSettingsUiLanguageTests()
    {
        _path = Path.GetTempFileName();
        AppSettings.SettingsPathOverride = _path;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_path); } catch { /* best-effort */ }
    }

    [Fact]
    public void UiLanguage_DefaultsToEn()
    {
        File.WriteAllText(_path, "{}");
        Assert.Equal("en", AppSettings.UiLanguage);
    }

    [Fact]
    public void UiLanguage_RoundTrips()
    {
        AppSettings.UiLanguage = "de";
        Assert.Equal("de", AppSettings.UiLanguage);
    }
}
