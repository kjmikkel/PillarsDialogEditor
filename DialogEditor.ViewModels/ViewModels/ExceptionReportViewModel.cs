using DialogEditor.Core.Localisation;

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

    // CopyText is never displayed — ExceptionReportWindow only puts it on the
    // clipboard, for the user to paste into a bug report on a public, English-language
    // issue tracker. Its labels sit beside an exception type and a stack trace that are
    // English whatever the UI language is, so translating them would only make a
    // maintainer's triage harder without helping the reporter.
    [NotLocalised("Crash-report text copied for a maintainer, not shown in the UI")]
    public ExceptionReportViewModel(Exception ex, string logPath, string issuesUrl)
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
    }
}
