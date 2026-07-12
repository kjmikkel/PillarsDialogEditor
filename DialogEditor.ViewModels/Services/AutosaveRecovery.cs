namespace DialogEditor.ViewModels.Services;

/// Whether a project has a recoverable autosave sidecar.
public enum AutosaveState { None, Stale, Newer }

/// The result of checking a project path for an autosave sidecar. SidecarTimeUtc
/// is set when the sidecar exists.
public sealed record AutosaveCheckResult(
    AutosaveState State, string SidecarPath, DateTime? SidecarTimeUtc);

/// Pure decisions about the autosave sidecar (`<project>.dialogproject.autosave`):
/// where it lives, whether it holds newer work than the saved project, and
/// best-effort deletion. Never throws — IO problems degrade to None + a warning.
/// Spec: docs/superpowers/specs/2026-07-12-autosave-design.md
public static class AutosaveRecovery
{
    public static string SidecarPath(string projectPath) => projectPath + ".autosave";

    public static AutosaveCheckResult Check(string projectPath)
    {
        var sidecar = SidecarPath(projectPath);
        try
        {
            if (!File.Exists(sidecar))
                return new AutosaveCheckResult(AutosaveState.None, sidecar, null);

            var sidecarTime = File.GetLastWriteTimeUtc(sidecar);

            // No project file but a sidecar: recover-only situation — offer it.
            if (!File.Exists(projectPath))
                return new AutosaveCheckResult(AutosaveState.Newer, sidecar, sidecarTime);

            var projectTime = File.GetLastWriteTimeUtc(projectPath);
            return sidecarTime > projectTime
                ? new AutosaveCheckResult(AutosaveState.Newer, sidecar, sidecarTime)
                : new AutosaveCheckResult(AutosaveState.Stale, sidecar, sidecarTime);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Autosave check failed for '{projectPath}': {ex.Message}");
            return new AutosaveCheckResult(AutosaveState.None, sidecar, null);
        }
    }

    /// Best-effort sidecar deletion; absence is fine, failure is logged.
    public static void TryDelete(string projectPath)
    {
        var sidecar = SidecarPath(projectPath);
        try
        {
            if (File.Exists(sidecar)) File.Delete(sidecar);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to delete autosave sidecar '{sidecar}': {ex.Message}");
        }
    }
}
