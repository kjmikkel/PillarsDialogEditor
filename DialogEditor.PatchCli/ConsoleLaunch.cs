using System.Runtime.InteropServices;
using DialogEditor.Core.Logging;

namespace DialogEditor.PatchCli;

/// How dialog-patcher was started, as far as the no-argument launch note cares (issue #77).
/// A seam so the tests can stand in for a real console.
public interface IConsoleLaunch
{
    /// True only when Windows created a console window just for this process — i.e. the
    /// exe was double-clicked in Explorer. False when run from a terminal, a script or with
    /// redirected input, so none of those ever get the note or the keypress wait.
    bool OwnsConsole { get; }

    void WaitForKey();
}

/// The real console. "Owns the console" means this process is the only one attached to it:
/// a terminal (cmd, pwsh, Windows Terminal) keeps its own shell attached alongside us, so the
/// count is at least 2 there. Checking redirected stdin alone would also fire when a user
/// types `dialog-patcher` in a terminal, and pausing there would just be in the way.
public sealed class SystemConsoleLaunch : IConsoleLaunch
{
    public bool OwnsConsole
    {
        get
        {
            if (!OperatingSystem.IsWindows() || Console.IsInputRedirected) return false;
            try
            {
                return GetConsoleProcessList(new uint[2], 2) == 1;
            }
            catch (Exception ex)
            {
                // Can't tell — fall back to the scripted behaviour, which is always safe.
                AppLog.Warn($"dialog-patcher: could not query the console process list: {ex.Message}");
                return false;
            }
        }
    }

    public void WaitForKey() => Console.ReadKey(intercept: true);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetConsoleProcessList(uint[] processList, uint processCount);
}
