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
