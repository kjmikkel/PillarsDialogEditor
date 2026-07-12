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

    private static bool IsBuildArtifact(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}");

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
            if (IsBuildArtifact(file)) continue;
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
}
