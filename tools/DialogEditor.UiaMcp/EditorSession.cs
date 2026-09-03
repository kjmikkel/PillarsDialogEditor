using System.Diagnostics;
using System.IO;
using System.Windows.Automation;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Holds the launched app across tool calls. UIA element references stay valid for as
/// long as the owning process lives, so a singleton session is sound.
/// </summary>
internal sealed class EditorSession
{
    private static readonly string DefaultSettings = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "settings.json");
    private static readonly string DefaultBackup = Path.Combine(
        Path.GetTempPath(), "PillarsDialogEditor.settings.backup.json");

    private readonly SettingsGuard _guard;
    private Process? _process;
    private UiaTree? _tree;

    public EditorSession() : this(new SettingsGuard(DefaultSettings, DefaultBackup)) { }

    public EditorSession(SettingsGuard guard)
    {
        _guard = guard;

        // Startup recovery is the PRIMARY safety net, not the backstop. Verified the
        // hard way: .NET does not run ProcessExit handlers when a process is hard-killed,
        // which is exactly how a wedged automation run dies — so the handler below misses
        // the case that matters most, and this call is what actually puts the user's
        // settings back.
        StartupRecovery = _guard.RecoverStaleBackup();

        // Still worth registering: it covers an orderly shutdown (stdin closed, SIGTERM),
        // restoring immediately rather than leaving it to the next server start.
        AppDomain.CurrentDomain.ProcessExit += (_, _) => SafeRestore();
    }

    public string? StartupRecovery { get; }
    public bool IsLive => _process is { HasExited: false };

    public string Launch(string repoRoot, string project)
    {
        var exe = Path.Combine(repoRoot, "DialogEditor.Avalonia", "bin", "Debug", "net8.0",
                               "DialogEditor.Avalonia.exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException($"NotBuilt: {exe} — run the build tool first.");

        _guard.Backup();
        _guard.SetLastProjectPath(project == "none" ? "" : project);

        _process = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true })
                   ?? throw new InvalidOperationException("Process.Start returned null.");

        var window = WaitForWindow(_process, TimeSpan.FromSeconds(30));
        _tree = new UiaTree(window);
        Foreground();
        return _process.MainWindowTitle;
    }

    private static AutomationElement WaitForWindow(Process p, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            p.Refresh();
            if (p.HasExited) throw new InvalidOperationException("AppExited during startup.");

            var win = AutomationElement.RootElement.FindFirst(TreeScope.Children,
                new PropertyCondition(AutomationElement.ProcessIdProperty, p.Id));
            if (win is not null) return win;

            Thread.Sleep(250);
        }
        throw new TimeoutException($"No UIA window appeared for PID {p.Id} within {timeout.TotalSeconds:n0}s.");
    }

    public void Foreground()
    {
        if (_process is null) return;
        Win32.SetForegroundWindow(_process.MainWindowHandle);
        Thread.Sleep(400);
    }

    public UiaTree Tree() => _tree
        ?? throw new InvalidOperationException("NoSession: call launch_app first.");

    public string Status()
    {
        if (_process is null) return "no session";
        _process.Refresh();
        return $"pid={_process.Id} exited={_process.HasExited} title='{_process.MainWindowTitle}'";
    }

    /// <summary>Kills the app FIRST — it writes settings on exit and would win the race.</summary>
    public string Kill()
    {
        try
        {
            if (_process is { HasExited: false }) { _process.Kill(); _process.WaitForExit(5000); }
        }
        finally
        {
            _process = null;
            _tree = null;
        }
        return _guard.Restore();
    }

    private void SafeRestore()
    {
        try { _guard.Restore(); }
        catch (Exception ex) { Console.Error.WriteLine($"Settings restore failed on exit: {ex}"); }
    }
}
