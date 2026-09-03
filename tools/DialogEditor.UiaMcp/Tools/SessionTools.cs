using System.ComponentModel;
using ModelContextProtocol.Server;

namespace DialogEditor.UiaMcp.Tools;

[McpServerToolType]
internal sealed class SessionTools(EditorSession session)
{
    [McpServerTool, Description("Launch the Dialog Editor and hold the session. Returns the window title.")]
    public string LaunchApp(
        [Description("Absolute path to the repository root")] string repoRoot,
        [Description("'none' for a projectless start, or an absolute .dialogproject path")] string project = "none",
        [Description("Kill an existing session first instead of refusing")] bool force = false)
    {
        if (session.IsLive && !force)
            return "Error(SessionAlreadyLive): an app session is already running. " +
                   "Call kill_app first, or pass force=true.";

        if (session.IsLive) session.Kill();

        var title = session.Launch(repoRoot, project);
        var prefix = session.StartupRecovery is { } r ? r + "\n" : "";
        return $"{prefix}launched: '{title}'";
    }

    [McpServerTool, Description("Report whether a live app session is held, with its pid and window title.")]
    public string SessionStatus() => session.Status();

    [McpServerTool, Description("Kill the app and restore the user's real settings.")]
    public string KillApp() => session.Kill();
}
