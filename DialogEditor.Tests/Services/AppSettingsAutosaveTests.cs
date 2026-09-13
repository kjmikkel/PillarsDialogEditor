using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// The two autosave knobs from issue #11. settings.json is hand-editable, so both
/// getters clamp defensively rather than trusting what they read back.
public class AppSettingsAutosaveTests : IDisposable
{
    public AppSettingsAutosaveTests()
        => AppSettings.SettingsPathOverride = Path.GetTempFileName();

    public void Dispose()
    {
        var path = AppSettings.SettingsPathOverride;
        AppSettings.SettingsPathOverride = null;
        if (path is not null && File.Exists(path)) File.Delete(path);
    }

    [Fact]
    public void AutosaveIntervalSeconds_DefaultsToTheHistoricalSixtySeconds()
        => Assert.Equal(60, AppSettings.AutosaveIntervalSeconds);

    [Fact]
    public void AutosaveIntervalSeconds_RoundTrips()
    {
        AppSettings.AutosaveIntervalSeconds = 300;
        Assert.Equal(300, AppSettings.AutosaveIntervalSeconds);
    }

    [Fact]
    public void AutosaveIntervalSeconds_ZeroMeansOff_AndSurvivesClamping()
    {
        AppSettings.AutosaveIntervalSeconds = 0;
        Assert.Equal(0, AppSettings.AutosaveIntervalSeconds);
    }

    [Theory]
    [InlineData(-5,     0)]      // negative is nonsense; the nearest sane meaning is "off"
    [InlineData(1,      5)]      // sub-5s would thrash the disk
    [InlineData(99999,  3600)]   // an hour is the ceiling
    public void AutosaveIntervalSeconds_ClampsOutOfRangeValues(int written, int expected)
    {
        AppSettings.AutosaveIntervalSeconds = written;
        Assert.Equal(expected, AppSettings.AutosaveIntervalSeconds);
    }

    [Fact]
    public void AutosaveGenerations_DefaultsToThree()
        => Assert.Equal(3, AppSettings.AutosaveGenerations);

    [Fact]
    public void AutosaveGenerations_RoundTrips()
    {
        AppSettings.AutosaveGenerations = 5;
        Assert.Equal(5, AppSettings.AutosaveGenerations);
    }

    [Theory]
    [InlineData(0,   1)]
    [InlineData(-3,  1)]
    [InlineData(500, 10)]
    public void AutosaveGenerations_ClampsOutOfRangeValues(int written, int expected)
    {
        AppSettings.AutosaveGenerations = written;
        Assert.Equal(expected, AppSettings.AutosaveGenerations);
    }
}
