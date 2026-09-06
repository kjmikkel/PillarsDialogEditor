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
    private UiaWindowSource? _windows;

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

    /// <summary>Shared across tool calls so refs minted by read_tree resolve in later calls.</summary>
    public RefTable Refs { get; } = new();

    public IntPtr WindowHandle => _process?.MainWindowHandle
        ?? throw new InvalidOperationException("NoSession: call launch_app first.");

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
        _windows = new UiaWindowSource(_process.Id);
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

    /// <summary>
    /// Every window this process currently shows, main window first-class rather than
    /// sole (#16). Re-enumerated on each call: a dialog can open or close between tool
    /// calls, and a stale list is how a run acts into a window that is already gone.
    /// </summary>
    public IReadOnlyList<WindowInfo> Windows()
    {
        if (_process is null || _windows is null)
            throw new InvalidOperationException("NoSession: call launch_app first.");
        return new WindowInventory(_windows, _process.MainWindowHandle).Windows();
    }

    /// <summary>
    /// Resolve a window selector and guard it, then hand back a tree rooted there. Every
    /// tool taking a `window` argument goes through here, so the modal guard is applied in
    /// one place rather than remembered at each call site.
    /// </summary>
    public bool TryWindow(string? selector, out WindowInfo window, out UiaTree tree, out string error)
    {
        window = null!;
        tree = null!;
        error = "";

        var result = new WindowResolver(Windows()).ResolveForUse(selector);
        if (result.ErrorKind is not null)
        {
            error = $"Error({result.ErrorKind}): {result.ErrorMessage}";
            return false;
        }

        window = result.Window!;
        tree = TreeFor(window);
        return true;
    }

    /// <summary>A census of open windows for read_tree; empty while only the main one is.</summary>
    public string WindowsSummary() => new WindowResolver(Windows()).Summary();

    /// <summary>
    /// Foreground a SPECIFIC window. The parameterless overload always targets the main
    /// window, which for a dialog would bring the wrong window forward and send synthetic
    /// input to it.
    /// </summary>
    public void Foreground(WindowInfo window)
    {
        Win32.SetForegroundWindow(window.Handle);
        Thread.Sleep(400);
    }

    /// <summary>A tree rooted at the given window, so inspection can leave the main one.</summary>
    public UiaTree TreeFor(WindowInfo window)
    {
        if (_windows is null)
            throw new InvalidOperationException("NoSession: call launch_app first.");

        // The main window keeps its long-lived tree: refs minted by an earlier read_tree
        // must still resolve, which a freshly built tree would break.
        return window.IsMain ? Tree() : new UiaTree(_windows.Element(window.Id));
    }

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
            _windows = null;
        }
        return _guard.Restore();
    }

    private void SafeRestore()
    {
        try { _guard.Restore(); }
        catch (Exception ex) { Console.Error.WriteLine($"Settings restore failed on exit: {ex}"); }
    }
}
