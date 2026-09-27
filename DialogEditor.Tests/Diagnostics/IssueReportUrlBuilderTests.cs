using DialogEditor.Core.Diagnostics;

namespace DialogEditor.Tests.Diagnostics;

/// <summary>
/// Issue #72: the exception report's "report" link opens GitHub's /issues/new with a
/// pre-filled, scrubbed title and body. The builder is pure — these tests only inspect
/// the returned string and never open a browser.
/// </summary>
public class IssueReportUrlBuilderTests
{
    private const string NewIssueUrl = "https://github.com/kjmikkel/PillarsDialogEditor/issues/new";
    private const string Profile     = @"C:\Users\Alice Smith";

    // Recognisable stand-ins for the localised templates, so assertions can find each part.
    private static readonly IssueReportText Text = new(
        TitleFormat:       "CRASH {0}: {1}",
        VersionLine:       "VERSION {0}",
        OperatingSystemLine: "OS {0}",
        ExceptionLine:     "EXCEPTION {0}",
        MessageHeading:    "MESSAGE",
        StackTraceHeading: "STACK",
        TruncatedMarker:   "TRUNCATED-SEE-LOG",
        LogFileLine:       "LOG {0}",
        StepsHeading:      "STEPS");

    private static readonly CrashReportScrubber Scrubber = new(
        [new ScrubRoot(Profile, "%USERPROFILE%")],
        [new ScrubWord("alicesmith", "%USERNAME%")]);

    private static IssueReportDetails Details(
        string message = "Something broke",
        string? stack = null,
        string type = "InvalidOperationException",
        string logPath = @"C:\Users\Alice Smith\AppData\Local\PillarsDialogEditor\app.log") => new(
            Version:           "1.2.3+abc",
            OperatingSystem:   "Microsoft Windows 10.0.26200",
            ExceptionTypeName: type,
            ExceptionFullName: "System." + type,
            Message:           message,
            StackTrace:        stack ?? StackOf(3),
            LogPath:           logPath);

    private static string StackOf(int lines, int width = 0) => string.Join(Environment.NewLine,
        Enumerable.Range(1, lines).Select(i =>
            $"   at Ns.Type.Method{i}() in {Profile}\\src\\F{i}.cs:line {i}" + new string('x', width)));

    private static string Build(IssueReportDetails d, int max = IssueReportUrlBuilder.DefaultMaxUrlLength)
        => IssueReportUrlBuilder.Build(NewIssueUrl, d, Text, Scrubber, max);

    /// Splits the query string and percent-decodes each value, the way GitHub reads it.
    private static Dictionary<string, string> Query(string url)
    {
        var query = new Uri(url).Query.TrimStart('?');
        return query.Split('&').Select(p => p.Split('=', 2))
            .ToDictionary(kv => kv[0], kv => Uri.UnescapeDataString(kv[1]));
    }

    // ── Shape ────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_TargetsTheNewIssuePage_WithTitleAndBodyOnly()
    {
        var url = Build(Details());

        Assert.StartsWith(NewIssueUrl + "?", url);
        Assert.Equal(["body", "title"], Query(url).Keys.Order().ToArray());
    }

    [Fact]
    public void Build_TitleNamesTheExceptionTypeAndMessage()
    {
        var title = Query(Build(Details()))["title"];

        Assert.Equal("CRASH InvalidOperationException: Something broke", title);
    }

    [Fact]
    public void Build_TitleUsesOnlyTheFirstLineOfAMultiLineMessage_AndIsCapped()
    {
        var title = Query(Build(Details(message: new string('m', 500) + "\nsecond line")))["title"];

        Assert.DoesNotContain("second line", title);
        Assert.True(title.Length <= IssueReportUrlBuilder.MaxTitleLength, $"title length {title.Length}");
        Assert.EndsWith("…", title);
    }

    [Fact]
    public void Build_BodyContainsVersionOsExceptionMessageStackAndSteps()
    {
        var body = Query(Build(Details()))["body"];

        Assert.Contains("VERSION 1.2.3+abc", body);
        Assert.Contains("OS Microsoft Windows 10.0.26200", body);
        Assert.Contains("EXCEPTION `System.InvalidOperationException`", body);
        Assert.Contains("Something broke", body);
        Assert.Contains("Method1()", body);
        Assert.Contains("Method3()", body);
        Assert.Contains("STEPS", body);
        Assert.Contains("LOG ", body);
        Assert.DoesNotContain("TRUNCATED-SEE-LOG", body);
    }

    [Fact]
    public void Build_BodyUsesUnixNewlines()
    {
        var body = Query(Build(Details()))["body"];

        Assert.DoesNotContain("\r", body);
    }

    // ── Scrubbing happens before the URL is built ────────────────────────────

    [Fact]
    public void Build_ScrubsProfilePathsFromMessageStackAndLogPath()
    {
        var url  = Build(Details(message: @"Could not read C:\Users\Alice Smith\mods\a.json"));
        var q    = Query(url);
        var all  = q["title"] + "\n" + q["body"];

        Assert.DoesNotContain("Alice", all, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(@"%USERPROFILE%\mods\a.json", all);
        Assert.Contains(@"%USERPROFILE%\src\F1.cs", all);
        Assert.Contains(@"%USERPROFILE%\AppData\Local\PillarsDialogEditor\app.log", all);
    }

    [Fact]
    public void Build_ScrubsTheUserNameFromTheTitle()
    {
        var title = Query(Build(Details(message: "Access denied for alicesmith")))["title"];

        Assert.Equal("CRASH InvalidOperationException: Access denied for %USERNAME%", title);
    }

    [Fact]
    public void Build_ScrubbedTextNeverAppearsEncodedInTheRawUrl()
    {
        // Belt and braces: the raw URL must not carry the name in encoded form either.
        var url = Build(Details(message: @"C:\Users\Alice Smith\x"));

        Assert.DoesNotContain("Alice", url, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(Uri.EscapeDataString("Alice Smith"), url, StringComparison.OrdinalIgnoreCase);
    }

    // ── URL encoding ─────────────────────────────────────────────────────────

    [Fact]
    public void Build_EncodesReservedCharacters_SoTheyCannotBreakTheQuery()
    {
        const string message = "a&b=c#d+e?f%g/h spaced";
        var url = Build(Details(message: message));

        // Exactly two parameters survive — & and = in the message did not split the query,
        // and # did not start a fragment.
        Assert.Equal(2, Query(url).Count);
        Assert.Equal(string.Empty, new Uri(url).Fragment);
        Assert.Contains(message, Query(url)["title"]);
        Assert.Contains(message, Query(url)["body"]);
        // '+' must be %2B: GitHub decodes a literal '+' in a query as a space.
        Assert.DoesNotContain("+", new Uri(url).Query);
        Assert.DoesNotContain(" ", url);
    }

    [Fact]
    public void Build_EncodesNewlinesAndNonAscii_AsUtf8PercentEscapes()
    {
        var url = Build(Details(message: "Zeile ä\nÆble 日本 🙂"));

        Assert.DoesNotContain("\n", url);
        Assert.Contains("%0A", url);
        Assert.Contains("%C3%A4", url);            // ä as UTF-8
        Assert.Contains("%F0%9F%99%82", url);      // 🙂 as UTF-8 (a surrogate pair in .NET)
        Assert.All(url, c => Assert.True(c < 128, $"non-ASCII char U+{(int)c:X4} in URL"));
        Assert.Contains("Zeile ä\nÆble 日本 🙂", Query(url)["body"]);
    }

    // ── Length cap ───────────────────────────────────────────────────────────

    [Fact]
    public void Build_KeepsOnlyTheTopOfALongStack_AndSaysSo()
    {
        var body = Query(Build(Details(stack: StackOf(200))))["body"];

        Assert.Contains("Method1()", body);
        Assert.DoesNotContain($"Method{IssueReportUrlBuilder.MaxStackLines + 1}()", body);
        Assert.Contains("TRUNCATED-SEE-LOG", body);
    }

    [Fact]
    public void Build_StaysUnderTheDefaultLimit_WithAHugeStack()
    {
        var url = Build(Details(stack: StackOf(IssueReportUrlBuilder.MaxStackLines, width: 400)));

        Assert.True(url.Length <= IssueReportUrlBuilder.DefaultMaxUrlLength, $"URL length {url.Length}");
        var body = Query(url)["body"];
        Assert.Contains("Method1()", body);          // the top frame survives
        Assert.Contains("TRUNCATED-SEE-LOG", body);  // and the reader is told where the rest is
    }

    [Fact]
    public void Build_TruncatesTheStackBeforeTheMessage()
    {
        var url  = Build(Details(message: "short and important", stack: StackOf(30, width: 300)),
                         max: 3000);
        var body = Query(url)["body"];

        Assert.True(url.Length <= 3000, $"URL length {url.Length}");
        Assert.Contains("short and important", body);
        Assert.Contains("TRUNCATED-SEE-LOG", body);
    }

    [Fact]
    public void Build_TruncatesAHugeMessage_WhenDroppingTheWholeStackIsNotEnough()
    {
        var url  = Build(Details(message: "HEAD" + new string('z', 20_000)));
        var body = Query(url)["body"];

        Assert.True(url.Length <= IssueReportUrlBuilder.DefaultMaxUrlLength, $"URL length {url.Length}");
        Assert.Contains("HEAD", body);
        Assert.Contains("TRUNCATED-SEE-LOG", body);
    }

    [Fact]
    public void Build_NeverSplitsASurrogatePair_WhenTruncating()
    {
        // 🙂 is two UTF-16 chars; a cut between them would produce an unencodable string.
        var url = Build(Details(message: string.Concat(Enumerable.Repeat("🙂", 5000))));

        Assert.True(url.Length <= IssueReportUrlBuilder.DefaultMaxUrlLength, $"URL length {url.Length}");
        Assert.DoesNotContain('\uFFFD', Query(url)["body"]);
    }

    [Fact]
    public void Build_Throws_WhenEvenTheSkeletonCannotFit()
    {
        // The view-model catches this and falls back to the plain issues list.
        Assert.Throws<InvalidOperationException>(() => Build(Details(), max: 100));
    }

    [Fact]
    public void Build_EmptyStack_OmitsNothingElse()
    {
        var body = Query(Build(Details(stack: "")))["body"];

        Assert.Contains("Something broke", body);
        Assert.DoesNotContain("TRUNCATED-SEE-LOG", body);
    }
}
