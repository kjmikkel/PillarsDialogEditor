using System.Text;

namespace DialogEditor.Core.Diagnostics;

/// <summary>The raw, unscrubbed facts about one crash.</summary>
public sealed record IssueReportDetails(
    string Version,
    string OperatingSystem,
    string ExceptionTypeName,   // short, for the title: "InvalidOperationException"
    string ExceptionFullName,   // qualified, for the body: "System.InvalidOperationException"
    string Message,
    string StackTrace,
    string LogPath);

/// <summary>
/// The localised labels of the pre-filled issue. Supplied by the caller (from Loc) so this
/// builder stays a pure function with no dependency on the resource system. Markdown
/// syntax (list bullets, headings, code fences) is added by the builder, never by the
/// translator. Placeholders: TitleFormat {0}=exception type, {1}=message first line;
/// VersionLine/OperatingSystemLine/ExceptionLine/LogFileLine {0}=the value.
/// </summary>
public sealed record IssueReportText(
    string TitleFormat,
    string VersionLine,
    string OperatingSystemLine,
    string ExceptionLine,
    string MessageHeading,
    string StackTraceHeading,
    string TruncatedMarker,
    string LogFileLine,
    string StepsHeading);

/// <summary>
/// Builds the GitHub "new issue" URL the exception report window opens (issue #72).
///
/// Pure: string in, string out; nothing is sent anywhere. The user reviews the pre-filled
/// issue in their browser and decides whether to submit it.
///
/// Order of operations is the point of the design:
///   1. Scrub every field (<see cref="CrashReportScrubber"/>) BEFORE anything is composed or
///      encoded, so no later step can reintroduce, or percent-encode past a check, a path.
///   2. Compose Markdown, keeping only the top <see cref="MaxStackLines"/> frames.
///   3. Encode with Uri.EscapeDataString (RFC 3986: UTF-8 percent escapes, space as %20 and
///      '+' as %2B; GitHub decodes a literal '+' in a query as a space).
///   4. If the encoded URL is over budget, shrink in order of least value to the reader:
///      drop stack frames from the bottom, then cut the message, each time adding the
///      localised "truncated, see the log" marker. The frames nearest the throw site and
///      the start of the message are what a maintainer triages on.
///
/// Why ~6000 characters: GitHub answers overly long /issues/new URLs with an error page
/// (around the 8 KB request-line mark), and some browser/shell handoffs are tighter still.
/// 6000 leaves headroom for the host part and for redirects through the login page, which
/// wrap the whole URL in a return_to parameter and encode it a second time.
///
/// No `template=` parameter: the repo has no .github/ISSUE_TEMPLATE yet. When a bug
/// template is added, add template= here; title/body still apply alongside it.
/// </summary>
public static class IssueReportUrlBuilder
{
    public const int DefaultMaxUrlLength = 6000;
    public const int MaxStackLines       = 20;
    public const int MaxTitleLength      = 120;

    private const string Ellipsis = "…";
    private const string Fence    = "```";

    public static string Build(
        string newIssueUrl,
        IssueReportDetails details,
        IssueReportText text,
        CrashReportScrubber scrubber,
        int maxUrlLength = DefaultMaxUrlLength)
    {
        // 1. Scrub first — everything below only ever sees scrubbed text.
        string Clean(string? s) => Normalise(scrubber.Scrub(s ?? string.Empty));

        var typeName = Clean(details.ExceptionTypeName);
        var fullName = Clean(details.ExceptionFullName);
        var message  = Clean(details.Message);
        var stack    = Clean(details.StackTrace)
            .Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Length > 0)
            .ToArray();

        var parts = new Parts(
            Title:   Title(text.TitleFormat, typeName, message),
            Version: Clean(details.Version),
            Os:      Clean(details.OperatingSystem),
            Type:    fullName,
            LogPath: Clean(details.LogPath));

        // Anything longer than the whole budget can never fit once encoded (encoding only
        // grows text), so cut it up front and keep the search below cheap.
        var messageCut = message.Length > maxUrlLength;
        if (messageCut) message = Cut(message, maxUrlLength);
        stack = stack.Select(l => l.Length > maxUrlLength ? Cut(l, maxUrlLength) : l).ToArray();

        // 2–4. Compose, encode, and shrink until it fits.
        var shown = Math.Min(stack.Length, MaxStackLines);
        string Url(int frames, string msg, bool truncated) =>
            Encode(newIssueUrl, parts.Title, Body(text, parts, msg, stack.Take(frames), truncated));

        var url = Url(shown, message, messageCut || shown < stack.Length);
        while (url.Length > maxUrlLength && shown > 0)
        {
            shown--;
            url = Url(shown, message, truncated: true);
        }
        if (url.Length <= maxUrlLength) return url;

        // Still too long with no frames at all: binary-search the longest message prefix
        // that fits.
        string? best = null;
        int lo = 0, hi = message.Length;
        while (lo <= hi)
        {
            var mid       = lo + (hi - lo) / 2;
            var candidate = Url(0, Cut(message, mid), truncated: true);
            if (candidate.Length <= maxUrlLength) { best = candidate; lo = mid + 1; }
            else hi = mid - 1;
        }
        return best ?? throw new InvalidOperationException(
            $"Issue report skeleton exceeds {maxUrlLength} characters even with an empty message.");
    }

    private sealed record Parts(string Title, string Version, string Os, string Type, string LogPath);

    private static string Title(string format, string typeName, string message)
    {
        var firstLine = message.Split('\n', 2)[0].Trim();
        var title     = string.Format(format, typeName, firstLine);
        return title.Length <= MaxTitleLength ? title : Cut(title, MaxTitleLength - Ellipsis.Length);
    }

    private static string Body(IssueReportText text, Parts p, string message,
        IEnumerable<string> frames, bool truncated)
    {
        var sb = new StringBuilder();
        sb.Append("- ").Append(string.Format(text.VersionLine, p.Version)).Append('\n');
        sb.Append("- ").Append(string.Format(text.OperatingSystemLine, p.Os)).Append('\n');
        sb.Append("- ").Append(string.Format(text.ExceptionLine, $"`{p.Type}`")).Append('\n');
        sb.Append('\n');

        sb.Append("### ").Append(text.MessageHeading).Append("\n\n");
        sb.Append(Fence).Append('\n').Append(message).Append('\n').Append(Fence).Append("\n\n");

        var frameList = frames.ToList();
        if (frameList.Count > 0)
        {
            sb.Append("### ").Append(text.StackTraceHeading).Append("\n\n");
            sb.Append(Fence).Append('\n');
            foreach (var f in frameList) sb.Append(f).Append('\n');
            sb.Append(Fence).Append("\n\n");
        }

        if (truncated) sb.Append(text.TruncatedMarker).Append("\n\n");

        sb.Append(string.Format(text.LogFileLine, $"`{p.LogPath}`")).Append("\n\n");
        sb.Append("### ").Append(text.StepsHeading).Append("\n\n");
        return sb.ToString();
    }

    private static string Encode(string baseUrl, string title, string body) =>
        $"{baseUrl}?title={Uri.EscapeDataString(title)}&body={Uri.EscapeDataString(body)}";

    // One newline convention: the URL carries %0A only, whatever the OS produced.
    private static string Normalise(string s) => s.Replace("\r\n", "\n").Replace('\r', '\n');

    /// Keeps the first <paramref name="length"/> chars and appends "…", never splitting a
    /// UTF-16 surrogate pair (half a pair cannot be UTF-8 encoded).
    private static string Cut(string s, int length)
    {
        if (length >= s.Length) return s;
        if (length > 0 && char.IsHighSurrogate(s[length - 1])) length--;
        return s[..length] + Ellipsis;
    }
}
