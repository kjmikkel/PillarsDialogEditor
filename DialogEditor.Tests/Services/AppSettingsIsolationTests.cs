using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

// Regression guard: the test run must never touch the user's real settings file.
// Many test classes drive AppSettings without setting their own SettingsPathOverride
// (and every class that does resets it to null in Dispose), so with no per-test
// override AppSettings must fall back to a test-run temp file — never to
// %LOCALAPPDATA%\PillarsDialogEditor\settings.json.
public class AppSettingsIsolationTests
{
    private static readonly string RealSettingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "settings.json");

    private static DateTime? RealFileStamp() =>
        File.Exists(RealSettingsPath) ? File.GetLastWriteTimeUtc(RealSettingsPath) : null;

    [Fact]
    public void WriteWithNoPerTestOverride_DoesNotTouchTheRealSettingsFile()
    {
        AppSettings.SettingsPathOverride = null;
        var before = RealFileStamp();

        // Round-trip an existing value: even if this leaked, the user's data survives.
        AppSettings.BrowserPinned = AppSettings.BrowserPinned;

        Assert.Equal(before, RealFileStamp());
    }
}
