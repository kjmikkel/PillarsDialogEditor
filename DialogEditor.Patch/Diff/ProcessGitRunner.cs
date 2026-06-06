using System.Diagnostics;
using System.Text;

namespace DialogEditor.Patch.Diff;

/// Real IGitRunner — invokes the `git` executable on PATH.
public sealed class ProcessGitRunner : IGitRunner
{
    public GitResult Run(string workingDirectory, params string[] args)
    {
        var psi = new ProcessStartInfo("git")
        {
            WorkingDirectory       = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true,
            // Git emits UTF-8; decode as such so non-ASCII content and a file's
            // UTF-8 BOM survive intact (the default console encoding mangles them).
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);

        try
        {
            using var p = Process.Start(psi)
                ?? throw new DiffException("Could not start git.");
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            return new GitResult(p.ExitCode, stdout, stderr);
        }
        catch (Exception ex) when (ex is not DiffException)
        {
            throw ClassifyStartFailure(ex);
        }
    }

    /// Maps a Process.Start failure to a DiffException. A Win32 "file not found"
    /// (error 2) means the git executable isn't on PATH → GitMissing; anything
    /// else stays generic. Exposed for unit testing (we can't summon a missing git).
    public static DiffException ClassifyStartFailure(Exception ex) =>
        ex is System.ComponentModel.Win32Exception { NativeErrorCode: 2 }   // ERROR_FILE_NOT_FOUND
            ? new DiffException($"git is not installed or not on PATH: {ex.Message}", DiffExceptionKind.GitMissing)
            : new DiffException($"git is not available: {ex.Message}", DiffExceptionKind.Unknown);
}
