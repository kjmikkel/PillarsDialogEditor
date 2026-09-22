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

    [ModuleInitializer]
    internal static void RedirectSettingsToTempFile()
    {
        AppSettings.DefaultSettingsPath = RunSettingsPath;
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try { File.Delete(RunSettingsPath); } catch { /* best-effort */ }
        };
    }
}
