using System.ComponentModel;
using System.Text;
using DialogEditor.UiaMcp.Core;
using ModelContextProtocol.Server;

namespace DialogEditor.UiaMcp.Tools;

[McpServerToolType]
internal sealed class BuildTools
{
    [McpServerTool, Description("Build the solution and return a compact result with diagnostics.")]
    public string Build(
        [Description("Absolute path to the repository root")] string repoRoot,
        [Description("Debug or Release")] string configuration = "Debug",
        [Description("Project or solution to build; defaults to DialogEditor.slnx")] string? project = null)
    {
        var target = project ?? "DialogEditor.slnx";
        var output = ProcessRunner.Run("dotnet",
            $"build \"{target}\" -c {configuration} --nologo",
            repoRoot, TimeSpan.FromMinutes(10));

        var r = BuildOutputParser.Parse(output);

        var sb = new StringBuilder();
        sb.AppendLine(r.Succeeded
            ? $"Build succeeded. {r.ErrorCount} error(s), {r.WarningCount} warning(s)."
            : $"Build FAILED. {r.ErrorCount} error(s), {r.WarningCount} warning(s).");

        // This server's own exe is part of DialogEditor.slnx, so a whole-solution build
        // launched THROUGH the server cannot overwrite it while it is running. Say so,
        // because MSB3021/MSB3027 otherwise reads as a code error.
        if (!r.Succeeded && output.Contains("DialogEditor.UiaMcp.exe", StringComparison.OrdinalIgnoreCase)
                         && (output.Contains("MSB3021") || output.Contains("MSB3027")
                             || output.Contains("being used by another process", StringComparison.OrdinalIgnoreCase)))
        {
            sb.AppendLine("NOTE: the failure is this server's own exe being locked by this running " +
                          "server, not a code error. Build a specific project instead, e.g. " +
                          "project=\"DialogEditor.Tests/DialogEditor.Tests.csproj\", or run " +
                          "`dotnet build` from a shell with the server stopped.");
        }

        foreach (var d in r.Diagnostics)
            sb.AppendLine($"  {d.Severity} {d.Code} {d.File}:{d.Line}:{d.Column} — {d.Message}");
        if (r.Truncated) sb.AppendLine("  … more diagnostics omitted.");
        return sb.ToString();
    }

    [McpServerTool, Description("Run the test suite and return counts plus named failures.")]
    public string RunTests(
        [Description("Absolute path to the repository root")] string repoRoot,
        [Description("xunit filter expression, e.g. FullyQualifiedName~DialogEditor.Tests.UiaMcp")] string? filter = null,
        [Description("Test project; defaults to DialogEditor.Tests")] string? project = null)
    {
        var target = project ?? "DialogEditor.Tests/DialogEditor.Tests.csproj";
        var args = $"test \"{target}\" --nologo";
        if (!string.IsNullOrWhiteSpace(filter)) args += $" --filter \"{filter}\"";

        var output = ProcessRunner.Run("dotnet", args, repoRoot, TimeSpan.FromMinutes(20));
        var r = TestOutputParser.Parse(output);

        var sb = new StringBuilder();
        sb.AppendLine(r.Succeeded
            ? $"Tests passed. {r.Passed} passed, {r.Skipped} skipped, {r.Total} total."
            : $"Tests FAILED. {r.Failed} failed, {r.Passed} passed, {r.Skipped} skipped, {r.Total} total.");
        foreach (var f in r.Failures)
            sb.AppendLine($"  FAILED {f.TestName}\n    {f.Message.Replace("\n", "\n    ")}");
        if (r.Truncated) sb.AppendLine("  … more failures omitted.");
        return sb.ToString();
    }
}
