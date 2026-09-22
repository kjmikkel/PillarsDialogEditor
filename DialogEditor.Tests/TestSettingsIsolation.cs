using System.Runtime.CompilerServices;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests;

// Assembly-wide safety net: before any test code runs, point AppSettings' fallback path
// at a per-run temp file so no test can read or rewrite the user's real
// %LOCALAPPDATA%\PillarsDialogEditor\settings.json — including the classes that never set
// SettingsPathOverride, and those whose Dispose resets it to null.
// Per-class overrides still take precedence; this only replaces the fallback.
// Guarded by Services/AppSettingsIsolationTests.
internal static class TestSettingsIsolation
{
    internal static readonly string RunSettingsPath = Path.Combine(
        Path.GetTempPath(), "PillarsDialogEditor.Tests", $"settings-{Environment.ProcessId}-{Guid.NewGuid():N}.json");

    // Same treatment for AppLog: every production catch block logs (CLAUDE.md), so without
    // this each test run appended hundreds of lines of test noise to the user's real
    // %LOCALAPPDATA%\PillarsDialogEditor\app.log. Guarded by Services/AppLogIsolationTests.
    internal static readonly string RunLogPath = Path.Combine(
        Path.GetTempPath(), "PillarsDialogEditor.Tests", $"app-{Environment.ProcessId}-{Guid.NewGuid():N}.log");

    [ModuleInitializer]
    internal static void RedirectSettingsToTempFile()
    {
        AppSettings.DefaultSettingsPath = RunSettingsPath;
        AppLog.LogPath = RunLogPath;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            // AppLog rotates to "<name>.log.old" past 1 MB; sweep that too.
            foreach (var path in new[] { RunSettingsPath, RunLogPath, Path.ChangeExtension(RunLogPath, ".log.old") })
                try { File.Delete(path); } catch { /* best-effort */ }
        };
    }
}
