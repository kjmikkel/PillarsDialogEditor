using System.Text.RegularExpressions;

namespace DialogEditor.UiaMcp.Core;

public record TestFailure(string TestName, string Message);

public record TestRunResult(
    bool Succeeded,
    int Passed,
    int Failed,
    int Skipped,
    int Total,
    IReadOnlyList<TestFailure> Failures,
    bool Truncated);

/// <summary>
/// Turns `dotnet test` console output into counts plus named failures. The suite runs
/// 2310 tests, so raw output is unusable as a tool result.
/// </summary>
public static class TestOutputParser
{
    private static readonly Regex SummaryPattern = new(
        @"(?<verdict>Passed|Failed)!\s+-\s+Failed:\s*(?<failed>\d+),\s*Passed:\s*(?<passed>\d+),\s*Skipped:\s*(?<skipped>\d+),\s*Total:\s*(?<total>\d+)",
        RegexOptions.Compiled);

    private static readonly Regex FailurePattern = new(
        @"^\s*Failed\s+(?<name>[^\s\[]+).*?$(?<body>(?:\n(?!\s*(?:Failed|Passed)\s).*)*)",
        RegexOptions.Compiled | RegexOptions.Multiline);

    public static TestRunResult Parse(string output, int maxFailures = 20)
    {
        var failures = new List<TestFailure>();
        foreach (Match m in FailurePattern.Matches(output))
        {
            var name = m.Groups["name"].Value.Trim();
            if (name.Length == 0) continue;
            failures.Add(new TestFailure(name, m.Groups["body"].Value.Trim()));
        }

        var summary = SummaryPattern.Match(output);
        if (!summary.Success)
        {
            // No summary line means the runner never completed. Never report that green.
            return new TestRunResult(false, 0, 0, 0, 0,
                failures.Take(maxFailures).ToList(), failures.Count > maxFailures);
        }

        return new TestRunResult(
            Succeeded: summary.Groups["verdict"].Value == "Passed",
            Passed: int.Parse(summary.Groups["passed"].Value),
            Failed: int.Parse(summary.Groups["failed"].Value),
            Skipped: int.Parse(summary.Groups["skipped"].Value),
            Total: int.Parse(summary.Groups["total"].Value),
            Failures: failures.Take(maxFailures).ToList(),
            Truncated: failures.Count > maxFailures);
    }
}
