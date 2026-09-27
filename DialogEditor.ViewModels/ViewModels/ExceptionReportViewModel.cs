using System.Runtime.InteropServices;
using DialogEditor.Core.Diagnostics;
using DialogEditor.Core.Localisation;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public sealed class ExceptionReportViewModel
{
    public string Title      { get; }
    public string TypeName   { get; }
    public string Message    { get; }
    public string StackTrace { get; }
    public string CopyText   { get; }
    public string LogPath    { get; }
    public string IssuesUrl  { get; }

    /// <summary>
    /// What the window's report button opens (issue #72): GitHub's /issues/new with a
    /// pre-filled, path-scrubbed title and body. Falls back to <see cref="IssuesUrl"/> (the
    /// plain issues list) if the pre-filled URL cannot be built, so the button always works.
    /// </summary>
    public string ReportUrl  { get; }

    // CopyText is never displayed — ExceptionReportWindow only puts it on the
    // clipboard, for the user to paste into a bug report on a public, English-language
    // issue tracker. Its labels sit beside an exception type and a stack trace that are
    // English whatever the UI language is, so translating them would only make a
    // maintainer's triage harder without helping the reporter.
    //
    // The pre-filled issue (ReportUrl) is different: the user reads and edits it in the
    // browser before submitting, so its labels ARE user-visible and come from Loc.
    //
    // reportUrlBuilder is a test seam; production passes null and gets
    // BuildDefaultReportUrl, which reads the real environment.
    [NotLocalised("Crash-report text copied for a maintainer, not shown in the UI")]
    public ExceptionReportViewModel(Exception ex, string logPath, string issuesUrl,
        Func<Exception, string>? reportUrlBuilder = null)
    {
        Title      = ex.GetType().Name;
        TypeName   = Title;
        Message    = ex.Message;
        StackTrace = ex.StackTrace ?? string.Empty;
        LogPath    = logPath;
        IssuesUrl  = issuesUrl;
        CopyText   = $"{TypeName}: {Message}{Environment.NewLine}{Environment.NewLine}" +
                     $"{StackTrace}{Environment.NewLine}{Environment.NewLine}" +
                     $"Issues: {issuesUrl}{Environment.NewLine}Log file: {logPath}";
        ReportUrl  = BuildReportUrl(ex, reportUrlBuilder ?? (e => BuildDefaultReportUrl(e, logPath, issuesUrl)));
    }

    private string BuildReportUrl(Exception ex, Func<Exception, string> builder)
    {
        try
        {
            return builder(ex);
        }
        catch (Exception buildError)
        {
            // Never let the crash reporter crash: the plain issues list is still useful.
            AppLog.Warn($"ExceptionReport: could not build the pre-filled issue URL, " +
                        $"falling back to the issues list — {buildError.GetType().Name}: {buildError.Message}");
            return IssuesUrl;
        }
    }

    // GitHub's new-issue page sits directly under the issues list.
    private static string BuildDefaultReportUrl(Exception ex, string logPath, string issuesUrl) =>
        IssueReportUrlBuilder.Build(
            issuesUrl.TrimEnd('/') + "/new",
            new IssueReportDetails(
                Version:           AppVersion.Current,
                OperatingSystem:   $"{RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})",
                ExceptionTypeName: ex.GetType().Name,
                ExceptionFullName: ex.GetType().FullName ?? ex.GetType().Name,
                Message:           ex.Message,
                StackTrace:        ex.StackTrace ?? string.Empty,
                LogPath:           logPath),
            LocalisedIssueText(),
            CrashReportScrubber.FromEnvironment());

    /// <summary>The pre-filled issue's labels, from the string resources.</summary>
    public static IssueReportText LocalisedIssueText() => new(
        TitleFormat:         Loc.Get("ExceptionReport_IssueTitle"),
        VersionLine:         Loc.Get("ExceptionReport_IssueVersion"),
        OperatingSystemLine: Loc.Get("ExceptionReport_IssueOs"),
        ExceptionLine:       Loc.Get("ExceptionReport_IssueException"),
        MessageHeading:      Loc.Get("ExceptionReport_IssueMessageHeading"),
        StackTraceHeading:   Loc.Get("ExceptionReport_IssueStackTraceHeading"),
        TruncatedMarker:     Loc.Get("ExceptionReport_IssueTruncated"),
        LogFileLine:         Loc.Get("ExceptionReport_IssueLogFile"),
        StepsHeading:        Loc.Get("ExceptionReport_IssueStepsHeading"));
}
