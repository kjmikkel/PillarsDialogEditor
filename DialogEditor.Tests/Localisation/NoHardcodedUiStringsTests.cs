using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// Enforcer for the CLAUDE.md localisation rule: no user-visible text may be
/// hard-coded in XAML — every label, tooltip, watermark, header, and title must
/// come from a resource (DynamicResource/StaticResource binding) so the app can
/// be translated without touching markup. The rule existed as prose only and
/// 14 literals accumulated across 7 views before this guard was added; like
/// NoStrayHexTests, it turns the convention into a build-breaking contract.
///
/// The scan flags text-bearing attributes whose value starts with a LETTER:
/// values starting with '{' are bindings (allowed), and symbol/number-only
/// values (e.g. Width="400", Content="+") are out of scope for this guard —
/// symbolic glyphs are governed by the tooltip rule instead.
///
/// StringFormat needs its own rule (issue #35): its value legitimately starts
/// with '{' ('{}{0} match(es)'), so the "starts with '{' means binding" escape
/// hatch above let literal English format strings through unseen. A StringFormat
/// passes only if it is a {StaticResource}/{DynamicResource} reference or, once
/// its {0}/{0:F1}/{} holes are removed, contains no letters at all.
/// </summary>
public class NoHardcodedUiStringsTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Build output and sibling worktrees (.claude/worktrees/<name>) are copies of
    // the tree at some other commit — scanning them reports stale offenders that
    // this checkout does not contain. Checked relative to root, not the absolute
    // path, so it doesn't misfire when root itself lives under a worktree.
    private static bool IsExcluded(string path, string root)
    {
        var segments = Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj")
            || segments.Contains(".worktrees") || segments.Contains("worktrees");
    }

    // Resource dictionaries are where the literal strings LIVE — they are the one
    // sanctioned home for user-visible text, so they are excluded from the scan.
    private static bool IsResourceDictionary(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}Resources{Path.DirectorySeparatorChar}");

    // Text-bearing attributes a user (or screen reader) can see. The value must
    // start with a letter to count: '{' means a binding, and pure symbols/numbers
    // (icon glyphs, sizes) are not translatable text.
    private static readonly Regex HardcodedText = new(
        @"[ <](Content|Text|Watermark|Header|Title)=""[A-Za-z][^""{]*""|ToolTip\.Tip=""[A-Za-z][^""{]*""",
        RegexOptions.Compiled);

    [Fact]
    public void NoHardcodedUserVisibleTextInAxaml()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root)) continue;
            if (IsResourceDictionary(file)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (HardcodedText.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0,
            "User-visible text must come from a resource dictionary (DynamicResource), never a XAML literal. Offenders:\n"
            + string.Join("\n", offenders));
    }

    // Three value shapes, tried in order: a resource reference (the sanctioned
    // pattern, e.g. DiffWindow's StringFormat={StaticResource Diff_NodeLine}), a
    // quoted literal ("..." on MultiBinding, '...' inside a {Binding}), or an
    // unquoted literal running to the next ',' or closing quote.
    private static readonly Regex StringFormatValue = new(
        @"StringFormat=(?:(?<res>\{(?:Static|Dynamic)Resource\b[^}]*\})|""(?<v>[^""]*)""|'(?<v>[^']*)'|(?<v>[^,""']*))",
        RegexOptions.Compiled);

    // {}, {0}, {1:F1} — the escape prefix and the substitution holes, whose
    // format specifiers (F1, N0) are letters that are not translatable text.
    private static readonly Regex FormatHole = new(@"\{[^{}]*\}", RegexOptions.Compiled);

    /// <summary>
    /// Returns the StringFormat values on <paramref name="line"/> that carry
    /// translatable text: anything other than a resource reference that still
    /// contains a letter once its holes are removed. XML entities are decoded
    /// first so '&amp;#x2014;' reads as the dash it is, not as the letter x.
    /// </summary>
    internal static IReadOnlyList<string> LiteralStringFormats(string line) =>
        StringFormatValue.Matches(line)
            .Where(m => !m.Groups["res"].Success)
            .Select(m => m.Groups["v"].Value)
            .Where(v => FormatHole.Replace(System.Net.WebUtility.HtmlDecode(v), "").Any(char.IsLetter))
            .ToList();

    [Theory]
    [InlineData("""Text="{Binding MatchCount, StringFormat='{}{0} match(es)'}" """)]
    [InlineData("""<MultiBinding StringFormat="  conversation '{0}' · node {1} · {2}">""")]
    [InlineData("""Text="{Binding Count, StringFormat=Total: {0}}" """)]
    public void LiteralStringFormats_FlagsFormatStringsContainingWords(string line) =>
        Assert.Single(LiteralStringFormats(line));

    [Theory]
    [InlineData("""Text="{Binding NodeId, StringFormat={StaticResource Diff_NodeLine}}" """)]
    [InlineData("""Text="{Binding NodeId, StringFormat={DynamicResource Diff_NodeLine}}" """)]
    [InlineData("""<MultiBinding StringFormat="{}{0} — {1}">""")]
    [InlineData("""Text="{Binding StringFormat='• {0}'}" """)]
    [InlineData("""Text="{Binding Avg, StringFormat='{}{0:F1}'}" """)]
    [InlineData("""Text="{Binding Avg, StringFormat={}{0:N0}}" """)]
    [InlineData("""<MultiBinding StringFormat="{}{0} &#x2014; {1}">""")]
    public void LiteralStringFormats_AllowsResourcesAndPunctuationOnlyFormats(string line) =>
        Assert.Empty(LiteralStringFormats(line));

    [Fact]
    public void NoLiteralStringFormatInAxaml()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root)) continue;
            if (IsResourceDictionary(file)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (var value in LiteralStringFormats(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: StringFormat \"{value}\"");
        }
        Assert.True(offenders.Count == 0,
            "A StringFormat that contains words is hard-coded UI text: put the whole format in a resource "
            + "and reference it (StringFormat={StaticResource Key}), or format it in the view model "
            + "(Loc.Format / Loc.FormatCount). Offenders:\n"
            + string.Join("\n", offenders));
    }
}
