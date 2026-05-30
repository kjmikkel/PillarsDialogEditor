using System.Diagnostics;

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
            // git missing / not on PATH
            throw new DiffException($"git is not available: {ex.Message}");
        }
    }
}
