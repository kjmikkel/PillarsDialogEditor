using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

public class BuildOutputParserTests
{
    // Shape taken verbatim from a real `dotnet build DialogEditor.slnx` run.
    private const string RealWarningLine =
        @"C:\repo\DialogEditor.ViewModels\Services\IVoAudioPlayer.cs(30,26): warning CS0067: The event 'NullVoAudioPlayer.PlaybackStopped' is never used [C:\repo\DialogEditor.ViewModels\DialogEditor.ViewModels.csproj]";

    private const string RealErrorLine =
        @"C:\repo\tools\SpikeUiaMcp\Program.cs(83,19): error CS0103: The name 'Path' does not exist in the current context [C:\repo\tools\SpikeUiaMcp\SpikeUiaMcp.csproj]";

    [Fact]
    public void ParsesASuccessfulBuildWithWarnings()
    {
        var output = $"{RealWarningLine}\n    8 Warning(s)\n    0 Error(s)\n\nBuild succeeded.\n";

        var result = BuildOutputParser.Parse(output);

        Assert.True(result.Succeeded);
        Assert.Equal(8, result.WarningCount);
        Assert.Equal(0, result.ErrorCount);
    }

    [Fact]
    public void ParsesAFailedBuild()
    {
        var output = $"{RealErrorLine}\n    0 Warning(s)\n    15 Error(s)\n\nBuild FAILED.\n";

        var result = BuildOutputParser.Parse(output);

        Assert.False(result.Succeeded);
        Assert.Equal(15, result.ErrorCount);
    }

    [Fact]
    public void ExtractsDiagnosticFileLineColumnCodeAndMessage()
    {
        var result = BuildOutputParser.Parse(RealErrorLine + "\nBuild FAILED.\n");

        var d = Assert.Single(result.Diagnostics);
        Assert.Equal("error", d.Severity);
        Assert.EndsWith("Program.cs", d.File);
        Assert.Equal(83, d.Line);
        Assert.Equal(19, d.Column);
        Assert.Equal("CS0103", d.Code);
        Assert.Contains("does not exist", d.Message);
    }

    [Fact]
    public void OrdersErrorsBeforeWarnings()
    {
        var output = $"{RealWarningLine}\n{RealErrorLine}\nBuild FAILED.\n";

        var result = BuildOutputParser.Parse(output);

        Assert.Equal("error", result.Diagnostics[0].Severity);
        Assert.Equal("warning", result.Diagnostics[1].Severity);
    }

    [Fact]
    public void DeduplicatesTheSameDiagnosticReportedOncePerTargetFramework()
    {
        var output = $"{RealErrorLine}\n{RealErrorLine}\nBuild FAILED.\n";

        var result = BuildOutputParser.Parse(output);

        Assert.Single(result.Diagnostics);
    }

    [Fact]
    public void TruncatesToMaxDiagnosticsAndFlagsIt()
    {
        var lines = Enumerable.Range(1, 30)
            .Select(i => $@"C:\repo\F{i}.cs({i},1): error CS0103: problem {i} [C:\repo\P.csproj]");
        var output = string.Join("\n", lines) + "\nBuild FAILED.\n";

        var result = BuildOutputParser.Parse(output, maxDiagnostics: 20);

        Assert.Equal(20, result.Diagnostics.Count);
        Assert.True(result.Truncated);
    }
}
