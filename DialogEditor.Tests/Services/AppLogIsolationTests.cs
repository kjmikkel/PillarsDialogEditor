using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

// Regression guard: the test run must never append to the user's real log file.
// CLAUDE.md requires every caught exception in production code to be logged, so every
// test that exercises an error path calls AppLog.Warn/Error — with no redirect, each run
// appended hundreds of lines of test noise to %LOCALAPPDATA%\PillarsDialogEditor\app.log,
// burying the entries a user would attach to a bug report.
public class AppLogIsolationTests
{
    private static readonly string RealLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "app.log");

    private static DateTime? RealFileStamp() =>
        File.Exists(RealLogPath) ? File.GetLastWriteTimeUtc(RealLogPath) : null;

    [Fact]
    public void Warn_DoesNotTouchTheRealLogFile()
    {
        var before = RealFileStamp();

        AppLog.Warn("AppLogIsolationTests: this line must not reach the real log");

        Assert.Equal(before, RealFileStamp());
    }

    [Fact]
    public void Warn_StillWritesToTheRedirectedLogFile()
    {
        var marker = $"AppLogIsolationTests-{Guid.NewGuid():N}";

        AppLog.Warn(marker);

        Assert.Contains(marker, File.ReadAllText(AppLog.LogPath));
    }
}
