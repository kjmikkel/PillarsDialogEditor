using DialogEditor.Core.GameData;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class LanguagePickerTests
{
    private static readonly IReadOnlyList<string> EnOnly    = ["en"];
    private static readonly IReadOnlyList<string> MultiLang = ["de", "en", "fr", "pl"];
    private static readonly IReadOnlyList<string> NoEn      = ["de", "fr", "pl"];

    [Fact]
    public void Pick_PreferredInList_ReturnsPreferred()
    {
        Assert.Equal("fr", LanguagePicker.Pick(MultiLang, "fr"));
    }

    [Fact]
    public void Pick_PreferredNotInList_FallsBackToEn()
    {
        Assert.Equal("en", LanguagePicker.Pick(MultiLang, "zh"));
    }

    [Fact]
    public void Pick_PreferredNull_FallsBackToEn()
    {
        Assert.Equal("en", LanguagePicker.Pick(MultiLang, null));
    }

    [Fact]
    public void Pick_PreferredEmpty_FallsBackToEn()
    {
        Assert.Equal("en", LanguagePicker.Pick(MultiLang, ""));
    }

    [Fact]
    public void Pick_NoEnAndPreferredNull_ReturnsFirstAvailable()
    {
        Assert.Equal("de", LanguagePicker.Pick(NoEn, null));
    }

    [Fact]
    public void Pick_NoEnAndPreferredAbsent_ReturnsFirstAvailable()
    {
        Assert.Equal("de", LanguagePicker.Pick(NoEn, "en"));
    }

    [Fact]
    public void Pick_OnlyEnglishAvailable_ReturnsEn()
    {
        Assert.Equal("en", LanguagePicker.Pick(EnOnly, null));
    }
}

public class AppSettingsPendingRestoreTests : IDisposable
{
    public AppSettingsPendingRestoreTests()
        => AppSettings.SettingsPathOverride = Path.GetTempFileName();

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    [Fact]
    public void GetPendingRestores_WhenNotSet_ReturnsNull()
    {
        Assert.Null(AppSettings.GetPendingRestores());
    }

    [Fact]
    public void SetPendingRestores_CanBeRetrieved()
    {
        var entries = new[]
        {
            new PendingRestoreEntry("bkConv1", "bkSt1", "origConv1", "origSt1"),
            new PendingRestoreEntry("bkConv2", "bkSt2", "origConv2", "origSt2"),
        };
        AppSettings.SetPendingRestores(entries);
        var got = AppSettings.GetPendingRestores();
        Assert.NotNull(got);
        Assert.Equal(2, got.Count);
        Assert.Equal("bkConv1",   got[0].BackupConvPath);
        Assert.Equal("origSt2",   got[1].OriginalStPath);
    }

    [Fact]
    public void ClearPendingRestores_ReturnsNull()
    {
        AppSettings.SetPendingRestores([new PendingRestoreEntry("a", "b", "c", "d")]);
        AppSettings.ClearPendingRestores();
        Assert.Null(AppSettings.GetPendingRestores());
    }
}

public class AppSettingsThemeTests : IDisposable
{
    public AppSettingsThemeTests()
        => AppSettings.SettingsPathOverride = Path.GetTempFileName();

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null) File.Delete(path);
    }

    [Fact]
    public void Theme_DefaultsToDark()
    {
        Assert.Equal("Dark", AppSettings.Theme);
    }

    [Fact]
    public void Theme_RoundTrips()
    {
        AppSettings.Theme = "HighContrast";
        Assert.Equal("HighContrast", AppSettings.Theme);
    }
}
