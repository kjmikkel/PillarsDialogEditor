using System.Diagnostics;
using System.Text;

namespace DialogEditor.UiaMcp;

internal static class ProcessRunner
{
    /// <summary>Runs a command, capturing stdout+stderr together. Returns combined output.</summary>
    public static string Run(string fileName, string arguments, string workingDirectory, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        using var p = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start {fileName}.");

        var sb = new StringBuilder();
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) sb.AppendLine(e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();

        if (!p.WaitForExit((int)timeout.TotalMilliseconds))
        {
            try { p.Kill(entireProcessTree: true); }
            catch (Exception ex) { Console.Error.WriteLine($"Failed to kill timed-out process: {ex}"); }
            sb.AppendLine($"[timed out after {timeout.TotalSeconds:n0}s]");
        }
        return sb.ToString();
    }
}
