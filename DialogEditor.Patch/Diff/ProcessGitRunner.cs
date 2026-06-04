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
            // git missing / not on PATH
            throw new DiffException($"git is not available: {ex.Message}");
        }
    }
}
