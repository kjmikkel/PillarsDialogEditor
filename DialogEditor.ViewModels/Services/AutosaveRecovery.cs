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
    /// The ceiling TryDelete sweeps to. Deliberately above the highest `keep` the
    /// Settings picker offers, so lowering the generations setting can never strand
    /// orphaned sidecars next to the project.
    private const int MaxGenerations = 10;

    /// The newest generation. Kept as the historical name so a sidecar written by a
    /// build that predated rotation is still found by Check.
    public static string SidecarPath(string projectPath) => projectPath + ".autosave";

    /// Path of the nth-newest autosave generation, 1-based. Generation 1 IS the
    /// historical `.autosave` sidecar (see SidecarPath); older ones carry the number.
    public static string GenerationPath(string projectPath, int generation)
        => generation <= 1 ? SidecarPath(projectPath) : $"{projectPath}.autosave.{generation}";

    /// Ages every existing generation by one and frees generation 1 for the incoming
    /// write, dropping whatever falls past `keep`. Called by AutosaveTick before it
    /// serializes. Best-effort like TryDelete: a rotation failure must never cost the
    /// caller its autosave, so it is logged and the write proceeds anyway.
    public static void Rotate(string projectPath, int keep)
    {
        if (keep < 1) keep = 1;
        try
        {
            // The oldest generation has nowhere left to go.
            var oldest = GenerationPath(projectPath, keep);
            if (File.Exists(oldest)) File.Delete(oldest);

            // Walk oldest-first so each move lands on a slot just vacated.
            for (var n = keep - 1; n >= 1; n--)
            {
                var from = GenerationPath(projectPath, n);
                if (File.Exists(from)) File.Move(from, GenerationPath(projectPath, n + 1), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to rotate autosave generations for '{projectPath}': {ex.Message}");
        }
    }

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

    /// Best-effort deletion of EVERY generation; absence is fine, failure is logged.
    /// Sweeps to MaxGenerations rather than the configured keep, so sidecars left
    /// behind by a previously higher setting are cleaned up too.
    public static void TryDelete(string projectPath)
    {
        for (var n = 1; n <= MaxGenerations; n++)
        {
            var sidecar = GenerationPath(projectPath, n);
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
}
