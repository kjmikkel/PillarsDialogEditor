using System.Text.RegularExpressions;

namespace DialogEditor.Tests.ViewModels;

/// Every catch block in MainWindowViewModel.cs that logs at Error severity must
/// also surface the exception via the ReportError delegate — or hand it to the
/// caller with `return ex;` (the CopyVoFolder pattern), or carry an explicit
/// `// error-window-exempt:` comment stating why the site has its own surfacing
/// (e.g. the PatchConflictException recovery dialog). Mirrors the
/// NoStrayHexTests source-scan idiom so a new status-bar-only error site fails
/// the build. Spec: docs/superpowers/specs/2026-07-05-error-window-non-save-design.md
public class ErrorReportingCoverageTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void EveryErrorLoggingCatchAlsoReportsToTheErrorWindow()
    {
        var path = Path.Combine(SolutionRoot(),
            "DialogEditor.ViewModels", "ViewModels", "MainWindowViewModel.cs");
        var source = File.ReadAllText(path);

        var offenders = new List<string>();
        foreach (Match m in Regex.Matches(source, @"catch\s*(\([^)]*\))?\s*\{"))
        {
            var block = ExtractBraceBlock(source, source.IndexOf('{', m.Index));
            if (!block.Contains("AppLog.Error")) continue;
            if (block.Contains("ReportError?.Invoke") || block.Contains("return ex;")
                || block.Contains("// error-window-exempt:")) continue;
            offenders.Add($"line {source[..m.Index].Count(c => c == '\n') + 1}");
        }

        Assert.True(offenders.Count == 0,
            "Catch blocks logging AppLog.Error without surfacing via ReportError "
            + "(add ReportError?.Invoke(ex); or `return ex;` with the caller invoking; "
            + "or a `// error-window-exempt: <why>` comment for sites with their own surfacing):\n"
            + string.Join('\n', offenders));
    }

    /// Returns the brace-delimited block starting at openBraceIndex (inclusive).
    private static string ExtractBraceBlock(string source, int openBraceIndex)
    {
        var depth = 0;
        for (var i = openBraceIndex; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}' && --depth == 0)
                return source.Substring(openBraceIndex, i - openBraceIndex + 1);
        }
        return source[openBraceIndex..];
    }
}
