using System.Security.Cryptography;
using System.Text;
using DialogEditor.Core.Logging;

namespace DialogEditor.ViewModels.Services;

/// Whether a project has a recoverable autosave sidecar.
public enum AutosaveState { None, Stale, Newer }

/// The result of checking a project path for an autosave sidecar. SidecarTimeUtc
/// is set when the sidecar exists. IsFallback is true when the copy found lives in the
/// per-user fallback folder rather than next to the project (issue 37).
public sealed record AutosaveCheckResult(
    AutosaveState State, string SidecarPath, DateTime? SidecarTimeUtc, bool IsFallback = false);

/// One autosave copy on disk: its file, when it was written, and whether it lives in the
/// per-user fallback folder (issue 37) rather than next to the project.
public sealed record AutosaveGeneration(string Path, DateTime TimeUtc, bool IsFallback);

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

    // ── Fallback location (issue 37) ─────────────────────────────────────────────
    // The sidecar belongs next to the project: it travels with it and there is never any doubt
    // which project it is for. But when the project's folder can't be written (read-only media,
    // a locked-down share, no permission), every tick used to fail silently and the user had no
    // crash protection at all. Autosave then falls back to a per-user folder instead. The file
    // name keeps the project's own name for a human browsing the folder, plus a hash of the full
    // path so two projects with the same file name can't collide.

    /// Test seam: replaces the per-user fallback folder.
    public static string? FallbackRootOverride { get; set; }

    public static string FallbackRoot => FallbackRootOverride ?? Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "autosave");

    public static string FallbackSidecarPath(string projectPath)
    {
        // Case-insensitive like the file system the editor runs on, so the same project opened
        // through a differently cased path finds its copy.
        var key = Path.GetFullPath(projectPath).ToUpperInvariant();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..16].ToLowerInvariant();
        return Path.Combine(FallbackRoot, $"{Path.GetFileName(projectPath).ToLowerInvariant()}.{hash}.autosave");
    }

    /// Path of the nth-newest generation, in the fallback folder when <paramref name="fallback"/>.
    public static string GenerationPath(string projectPath, int generation, bool fallback)
    {
        if (!fallback) return GenerationPath(projectPath, generation);
        var first = FallbackSidecarPath(projectPath);
        return generation <= 1 ? first : $"{first}.{generation}";
    }

    /// True when a write failed because of WHERE it was written (no access, missing folder,
    /// write-protected volume), not because of a passing moment such as a sharing violation.
    /// Only these move autosave to the fallback folder.
    public static bool IsLocationFailure(Exception ex) => ex switch
    {
        UnauthorizedAccessException => true,
        DirectoryNotFoundException  => true,
        IOException io              => io.HResult == unchecked((int)0x80070013),   // ERROR_WRITE_PROTECT
        _                           => false,
    };

    /// Ages every existing generation by one and frees generation 1 for the incoming
    /// write, dropping whatever falls past `keep`. Called by AutosaveTick before it
    /// serializes. Best-effort like TryDelete: a rotation failure must never cost the
    /// caller its autosave, so it is logged and the write proceeds anyway.
    public static void Rotate(string projectPath, int keep) => Rotate(projectPath, keep, fallback: false);

    public static void Rotate(string projectPath, int keep, bool fallback)
    {
        if (keep < 1) keep = 1;
        try
        {
            // The oldest generation has nowhere left to go.
            var oldest = GenerationPath(projectPath, keep, fallback);
            if (File.Exists(oldest)) File.Delete(oldest);

            // Walk oldest-first so each move lands on a slot just vacated.
            for (var n = keep - 1; n >= 1; n--)
            {
                var from = GenerationPath(projectPath, n, fallback);
                if (File.Exists(from)) File.Move(from, GenerationPath(projectPath, n + 1, fallback), overwrite: true);
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Failed to rotate autosave generations for '{projectPath}': {ex.Message}");
        }
    }

    /// Checks the sidecar next to the project and the fallback copy (issue 37), and returns
    /// whichever holds the newer autosave.
    public static AutosaveCheckResult Check(string projectPath)
    {
        var local    = CheckAt(projectPath, SidecarPath(projectPath), isFallback: false);
        var fallback = CheckAt(projectPath, FallbackSidecarPath(projectPath), isFallback: true);
        if (fallback.State == AutosaveState.None) return local;
        if (local.State == AutosaveState.None) return fallback;
        return fallback.SidecarTimeUtc > local.SidecarTimeUtc ? fallback : local;
    }

    private static AutosaveCheckResult CheckAt(string projectPath, string sidecar, bool isFallback)
    {
        try
        {
            if (!File.Exists(sidecar))
                return new AutosaveCheckResult(AutosaveState.None, sidecar, null, isFallback);

            var sidecarTime = File.GetLastWriteTimeUtc(sidecar);

            // No project file but a sidecar: recover-only situation — offer it.
            if (!File.Exists(projectPath))
                return new AutosaveCheckResult(AutosaveState.Newer, sidecar, sidecarTime, isFallback);

            var projectTime = File.GetLastWriteTimeUtc(projectPath);
            return sidecarTime > projectTime
                ? new AutosaveCheckResult(AutosaveState.Newer, sidecar, sidecarTime, isFallback)
                : new AutosaveCheckResult(AutosaveState.Stale, sidecar, sidecarTime, isFallback);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"Autosave check failed for '{projectPath}': {ex.Message}");
            return new AutosaveCheckResult(AutosaveState.None, sidecar, null, isFallback);
        }
    }

    /// Every autosave copy of the project, next to it and in the fallback folder, newest first
    /// (issue 36: the restore offer lets the user fall back to an earlier copy when the newest
    /// is damaged or caught a mistake). Never throws: an unreadable copy is just left out.
    public static IReadOnlyList<AutosaveGeneration> ListGenerations(string projectPath)
    {
        var found = new List<AutosaveGeneration>();
        foreach (var fallback in new[] { false, true })
        for (var n = 1; n <= MaxGenerations; n++)
        {
            var path = GenerationPath(projectPath, n, fallback);
            try
            {
                if (File.Exists(path)) found.Add(new AutosaveGeneration(path, File.GetLastWriteTimeUtc(path), fallback));
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Could not read autosave copy '{path}': {ex.Message}");
            }
        }
        return found.OrderByDescending(g => g.TimeUtc).ToList();
    }

    /// Best-effort deletion of EVERY generation, next to the project and in the fallback
    /// folder (issue 37: stale central copies must not accumulate); absence is fine, failure
    /// is logged. Sweeps to MaxGenerations rather than the configured keep, so sidecars left
    /// behind by a previously higher setting are cleaned up too.
    public static void TryDelete(string projectPath)
    {
        foreach (var fallback in new[] { false, true })
        for (var n = 1; n <= MaxGenerations; n++)
        {
            var sidecar = GenerationPath(projectPath, n, fallback);
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
