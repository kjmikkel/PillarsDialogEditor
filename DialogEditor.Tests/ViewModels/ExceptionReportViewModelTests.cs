using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class ExceptionReportViewModelTests
{
    private const string LogPath   = @"C:\logs\app.log";
    private const string IssuesUrl = "https://example.com/issues";

    public ExceptionReportViewModelTests() => Loc.Configure(new StubStringProvider());

    private static ExceptionReportViewModel Make(Exception ex)
        => new(ex, LogPath, IssuesUrl);

    // Throw and catch so the exception carries a real stack trace.
    private static Exception WithStack()
    {
        try { throw new InvalidOperationException("oops"); }
        catch (Exception ex) { return ex; }
    }

    [Fact]
    public void Title_IsSimpleTypeName_NoNamespace()
    {
        var vm = Make(new InvalidOperationException("msg"));
        Assert.Equal("InvalidOperationException", vm.Title);
    }

    [Fact]
    public void TypeName_MatchesTitle()
    {
        var vm = Make(new ArgumentNullException("p"));
        Assert.Equal(vm.Title, vm.TypeName);
    }

    [Fact]
    public void Message_IsExceptionMessage()
    {
        var vm = Make(new InvalidOperationException("something broke"));
        Assert.Equal("something broke", vm.Message);
    }

    [Fact]
    public void StackTrace_ContainsAtLines_WhenExceptionWasThrown()
    {
        var vm = Make(WithStack());
        Assert.Contains("at ", vm.StackTrace);
    }

    [Fact]
    public void StackTrace_IsEmpty_WhenExceptionWasNeverThrown()
    {
        var vm = Make(new InvalidOperationException("not thrown"));
        Assert.Equal(string.Empty, vm.StackTrace);
    }

    [Fact]
    public void CopyText_ContainsTypeName()
    {
        var vm = Make(WithStack());
        Assert.Contains("InvalidOperationException", vm.CopyText);
    }

    [Fact]
    public void CopyText_ContainsMessage()
    {
        var vm = Make(new Exception("specific message"));
        Assert.Contains("specific message", vm.CopyText);
    }

    [Fact]
    public void CopyText_ContainsLogPath()
    {
        var vm = Make(new Exception("x"));
        Assert.Contains(LogPath, vm.CopyText);
    }

    [Fact]
    public void CopyText_ContainsIssuesUrl()
    {
        var vm = Make(new Exception("x"));
        Assert.Contains(IssuesUrl, vm.CopyText);
    }

    [Fact]
    public void LogPath_IsPassedValue()
    {
        var vm = Make(new Exception("x"));
        Assert.Equal(LogPath, vm.LogPath);
    }

    [Fact]
    public void IssuesUrl_IsPassedValue()
    {
        var vm = Make(new Exception("x"));
        Assert.Equal(IssuesUrl, vm.IssuesUrl);
    }

    // ── Issue #72: pre-filled "new issue" link ───────────────────────────────

    [Fact]
    public void ReportUrl_ComesFromTheInjectedBuilder()
    {
        var ex = new Exception("x");
        Exception? seen = null;

        var vm = new ExceptionReportViewModel(ex, LogPath, IssuesUrl,
            reportUrlBuilder: e => { seen = e; return "https://example.com/issues/new?title=t"; });

        Assert.Same(ex, seen);
        Assert.Equal("https://example.com/issues/new?title=t", vm.ReportUrl);
    }

    [Fact]
    public void ReportUrl_FallsBackToTheIssuesList_WhenBuildingFails()
    {
        var vm = new ExceptionReportViewModel(new Exception("x"), LogPath, IssuesUrl,
            reportUrlBuilder: _ => throw new InvalidOperationException("boom"));

        Assert.Equal(IssuesUrl, vm.ReportUrl);
    }

    [Fact]
    public void ReportUrl_ByDefault_OpensTheNewIssuePageWithTitleAndBody()
    {
        var vm = Make(WithStack());

        Assert.StartsWith(IssuesUrl + "/new?", vm.ReportUrl);
        Assert.Contains("title=", vm.ReportUrl);
        Assert.Contains("body=", vm.ReportUrl);
        Assert.Contains("oops", Uri.UnescapeDataString(vm.ReportUrl));
    }

    [Fact]
    public void ReportUrl_ByDefault_ContainsNoLocalProfilePathOrUserName()
    {
        // The real environment: whatever machine this runs on, its profile path and
        // account name must not survive into a URL bound for a public tracker.
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Ed", "app.log");
        var ex = new IOException($"Could not open {Path.Combine(profile, "Documents", "a.json")}");

        var decoded = Uri.UnescapeDataString(
            new ExceptionReportViewModel(ex, logPath, IssuesUrl).ReportUrl);

        Assert.StartsWith(IssuesUrl + "/new?", decoded);
        Assert.DoesNotContain(profile, decoded, StringComparison.OrdinalIgnoreCase);
        if (Environment.UserName.Length >= DialogEditor.Core.Diagnostics.CrashReportScrubber.MinWordLength)
            Assert.DoesNotContain(Environment.UserName, decoded, StringComparison.OrdinalIgnoreCase);
    }
}
