using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

public class TestOutputParserTests
{
    [Fact]
    public void ParsesAPassingRunSummary()
    {
        const string output =
            "Passed!  - Failed:     0, Passed:   219, Skipped:     0, Total:   219, Duration: 4 s\n";

        var result = TestOutputParser.Parse(output);

        Assert.True(result.Succeeded);
        Assert.Equal(219, result.Passed);
        Assert.Equal(0, result.Failed);
        Assert.Equal(219, result.Total);
    }

    [Fact]
    public void ParsesAFailingRunSummary()
    {
        const string output =
            "Failed!  - Failed:     3, Passed:   216, Skipped:     1, Total:   220, Duration: 5 s\n";

        var result = TestOutputParser.Parse(output);

        Assert.False(result.Succeeded);
        Assert.Equal(3, result.Failed);
        Assert.Equal(216, result.Passed);
        Assert.Equal(1, result.Skipped);
    }

    [Fact]
    public void ExtractsFailingTestNamesAndAssertionMessages()
    {
        const string output = """
              Failed DialogEditor.Tests.UiaMcp.SettingsGuardTests.RestoreCopiesBackupBackAndRemovesIt [12 ms]
              Error Message:
               Assert.True() Failure
              Expected: True
              Actual:   False
              Failed!  - Failed:     1, Passed:     0, Skipped:     0, Total:     1, Duration: 12 ms
            """;

        var result = TestOutputParser.Parse(output);

        var failure = Assert.Single(result.Failures);
        Assert.Equal(
            "DialogEditor.Tests.UiaMcp.SettingsGuardTests.RestoreCopiesBackupBackAndRemovesIt",
            failure.TestName);
        Assert.Contains("Assert.True() Failure", failure.Message);
    }

    [Fact]
    public void TruncatesFailureListAndFlagsIt()
    {
        var lines = Enumerable.Range(1, 25).Select(i =>
            $"  Failed Some.Namespace.Test{i} [1 ms]\n  Error Message:\n   boom {i}");
        var output = string.Join("\n", lines) +
                     "\nFailed!  - Failed:    25, Passed:     0, Skipped:     0, Total:    25\n";

        var result = TestOutputParser.Parse(output, maxFailures: 20);

        Assert.Equal(20, result.Failures.Count);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void ReportsNoSummaryAsAFailedRunRatherThanASilentPass()
    {
        // A crashed runner prints no summary line. Treating that as success would be
        // the worst possible default — it would report green for a run that never ran.
        var result = TestOutputParser.Parse("MSBUILD : error MSB1009: Project file does not exist.\n");

        Assert.False(result.Succeeded);
        Assert.Equal(0, result.Total);
    }
}
