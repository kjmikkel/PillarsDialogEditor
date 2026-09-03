using System.Text.RegularExpressions;

namespace DialogEditor.UiaMcp.Core;

public record Diagnostic(string Severity, string File, int Line, int Column, string Code, string Message);

public record BuildResult(
    bool Succeeded,
    int ErrorCount,
    int WarningCount,
    IReadOnlyList<Diagnostic> Diagnostics,
    bool Truncated);

/// <summary>
/// Turns `dotnet build` console output into a compact result. Raw output is far too
/// noisy to hand back verbatim: a clean Debug build of this solution still emits
/// eight warnings spread over dozens of lines, each repeated per target framework.
/// </summary>
public static class BuildOutputParser
{
    private static readonly Regex DiagnosticPattern = new(
        @"^(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s+(?<sev>error|warning)\s+(?<code>[A-Z]+\d+):\s+(?<msg>.*?)(?:\s+\[[^\]]*\])?$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    private static readonly Regex SummaryPattern = new(
        @"^\s*(?<n>\d+)\s+(?<kind>Warning|Error)\(s\)\s*$",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static BuildResult Parse(string output, int maxDiagnostics = 20)
    {
        var all = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (Match m in DiagnosticPattern.Matches(output))
        {
            // Deduplicate on severity+code+location rather than the whole line. MSBuild
            // repeats a diagnostic once per target framework (identical lines), but it also
            // restates ONE problem many times with a varying counter — a locked output file
            // emits ten MSB3026 "Beginning retry N" warnings from the same targets line.
            // Keying on the message text would let those flood the list.
            var key = string.Join('|',
                m.Groups["sev"].Value, m.Groups["code"].Value,
                m.Groups["file"].Value.Trim(), m.Groups["line"].Value, m.Groups["col"].Value);
            if (!seen.Add(key)) continue;

            all.Add(new Diagnostic(
                m.Groups["sev"].Value,
                m.Groups["file"].Value.Trim(),
                int.Parse(m.Groups["line"].Value),
                int.Parse(m.Groups["col"].Value),
                m.Groups["code"].Value,
                m.Groups["msg"].Value.Trim()));
        }

        var errorCount = 0;
        var warningCount = 0;
        foreach (Match m in SummaryPattern.Matches(output))
        {
            var n = int.Parse(m.Groups["n"].Value);
            if (m.Groups["kind"].Value == "Error") errorCount = n; else warningCount = n;
        }

        var ordered = all
            .OrderBy(d => d.Severity == "error" ? 0 : 1)
            .ThenBy(d => d.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Line)
            .ToList();

        var succeeded = output.Contains("Build succeeded", StringComparison.OrdinalIgnoreCase)
                        && !output.Contains("Build FAILED", StringComparison.OrdinalIgnoreCase);

        return new BuildResult(
            succeeded,
            errorCount,
            warningCount,
            ordered.Take(maxDiagnostics).ToList(),
            Truncated: ordered.Count > maxDiagnostics);
    }
}
