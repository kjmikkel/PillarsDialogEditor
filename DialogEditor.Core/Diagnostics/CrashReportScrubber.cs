using System.Text.RegularExpressions;

namespace DialogEditor.Core.Diagnostics;

/// <summary>A directory whose path, and anything under it, is replaced by <see cref="Placeholder"/>.</summary>
public sealed record ScrubRoot(string Path, string Placeholder);

/// <summary>A bare token (account name, machine name) replaced wherever it stands as a whole word.</summary>
public sealed record ScrubWord(string Value, string Placeholder);

/// <summary>
/// Removes machine-identifying text from crash-report content before it is put in a
/// pre-filled GitHub issue URL (issue #72). The repository is public, so the issue text is
/// world-readable: the reporter's profile path (and with it their account name) must never
/// reach it, even though they get to review the text before submitting.
///
/// Two passes, in this order:
///   1. Roots — directory paths, longest first, so a specific root such as %LOCALAPPDATA%
///      wins over the %USERPROFILE% that contains it and the report still says roughly where
///      a file lived. Matching is case-insensitive and accepts '\', '/' and runs of them, so
///      "C:/Users/x" (file URIs) and "C:\\Users\\x" (escaped JSON in a message) are caught.
///      A root only matches at a path-segment boundary: "C:\Users\Al" must not eat the
///      front of "C:\Users\Alice".
///   2. Words — the bare account and machine names, as whole words, case-insensitively, as a
///      backstop for places a path rule can't see (a UNC share, an "access denied for x"
///      message, an 8.3 short name like ALICE~1).
///
/// Placeholders are single %WORD% tokens, so pass 2 can never match inside one a pass-1
/// replacement produced (word boundaries are letters/digits, and every placeholder letter
/// run is bounded by '%').
/// </summary>
public sealed class CrashReportScrubber
{
    /// <summary>
    /// Words shorter than this are not scrubbed: a one- or two-letter account name ("a",
    /// "at") would shred ordinary stack-trace text while adding no privacy the path rules
    /// don't already give — the profile path, where such a name actually appears, is
    /// still replaced whole.
    /// </summary>
    public const int MinWordLength = 3;

    private readonly IReadOnlyList<(Regex Pattern, string Placeholder)> _rules;

    public CrashReportScrubber(IEnumerable<ScrubRoot> roots, IEnumerable<ScrubWord> words)
    {
        var rootRules = roots
            .Select(r => (Segments: Segments(r.Path), Root: r))
            .Where(r => IsScrubbableRoot(r.Segments))
            // Longest path first: the most specific root wins.
            .OrderByDescending(r => r.Segments.Sum(s => s.Length + 1))
            .Select(r => (RootPattern(r.Root.Path, r.Segments), r.Root.Placeholder));

        var wordRules = words
            .Where(w => !string.IsNullOrWhiteSpace(w.Value) && w.Value.Trim().Length >= MinWordLength)
            .OrderByDescending(w => w.Value.Length)
            .Select(w => (WordPattern(w.Value.Trim()), w.Placeholder));

        _rules = rootRules.Concat(wordRules).ToList();
    }

    public string Scrub(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        foreach (var (pattern, placeholder) in _rules)
            text = pattern.Replace(text, placeholder.Replace("$", "$$"));
        return text;
    }

    /// <summary>
    /// The scrubber for this machine: the profile and the per-user folders that usually
    /// live under it (so a log or settings path keeps a readable %LOCALAPPDATA%-style
    /// prefix), plus the account and machine names. Game installs under the profile are
    /// covered by the profile root; installs elsewhere (D:\GOG Games\…) identify nobody.
    /// Deliberately no try/catch: Core has no logger, so a failure here propagates to
    /// ExceptionReportViewModel, which logs it and falls back to the plain issues list.
    /// </summary>
    public static CrashReportScrubber FromEnvironment()
    {
        var roots = new List<ScrubRoot>
        {
            new(Folder(Environment.SpecialFolder.UserProfile),          "%USERPROFILE%"),
            new(Folder(Environment.SpecialFolder.ApplicationData),      "%APPDATA%"),
            new(Folder(Environment.SpecialFolder.LocalApplicationData), "%LOCALAPPDATA%"),
            // Documents may be redirected (OneDrive, another drive) and still carry the name.
            new(Folder(Environment.SpecialFolder.MyDocuments),          "%DOCUMENTS%"),
            new(Environment.GetEnvironmentVariable("OneDrive") ?? "",   "%OneDrive%"),
            new(Path.GetTempPath(),                                     "%TEMP%"),
        };

        var words = new List<ScrubWord>
        {
            new(Environment.UserName,    "%USERNAME%"),
            // The profile folder name can differ from the account name (renamed accounts,
            // Microsoft-account truncation like "kjmik"), and it is what paths carry.
            new(LastSegment(Folder(Environment.SpecialFolder.UserProfile)), "%USERNAME%"),
            new(Environment.MachineName, "%COMPUTERNAME%"),
        };

        return new CrashReportScrubber(roots, words);
    }

    // ── Pattern construction ────────────────────────────────────────────────

    private static readonly char[] Separators = ['\\', '/'];

    private static string[] Segments(string? path) =>
        string.IsNullOrWhiteSpace(path)
            ? []
            : path.Trim().Split(Separators, StringSplitOptions.RemoveEmptyEntries);

    // A bare filesystem root ("C:\", "/") would turn every path in the trace into a
    // placeholder — never useful, and a misconfigured TEMP could cause it.
    private static bool IsScrubbableRoot(string[] segments) =>
        segments.Length > 1 || (segments.Length == 1 && !segments[0].EndsWith(':'));

    private static Regex RootPattern(string path, string[] segments)
    {
        var sep     = @"[\\/]+";
        var leading = Separators.Contains(path.Trim()[0]) ? sep : @"(?<![\p{L}\p{N}_])";
        var body    = string.Join(sep, segments.Select(Regex.Escape));
        // End at a segment boundary: a separator, the end, or a character that cannot be
        // part of a folder name in running text (quotes, whitespace, brackets, punctuation).
        const string end = @"(?=$|[\\/\s""'`<>|:;,)\]}])";
        return new Regex(leading + body + end,
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static Regex WordPattern(string word) =>
        new(@"(?<![\p{L}\p{N}])" + Regex.Escape(word) + @"(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string Folder(Environment.SpecialFolder folder) =>
        Environment.GetFolderPath(folder, Environment.SpecialFolderOption.DoNotVerify);

    private static string LastSegment(string path) => Segments(path).LastOrDefault() ?? "";
}
