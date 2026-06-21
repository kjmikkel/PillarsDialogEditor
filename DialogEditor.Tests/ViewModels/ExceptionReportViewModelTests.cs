using DialogEditor.ViewModels;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class ExceptionReportViewModelTests
{
    private const string LogPath   = @"C:\logs\app.log";
    private const string IssuesUrl = "https://example.com/issues";

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
}
