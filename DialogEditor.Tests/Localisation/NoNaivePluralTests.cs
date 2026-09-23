using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// Structural guard for the 2026-07-04 pluralisation work: the "(s)"/"(es)" idiom
/// cannot be translated into languages with more than two plural forms (Polish 4,
/// Arabic 6). Pluralised strings use _One/_Few/_Many/_Other… key pairs resolved by
/// Loc.FormatCount (see docs/superpowers/specs/2026-07-04-pluralisation-design.md);
/// this test keeps the old idiom from creeping back into the dictionaries.
///
/// The dictionaries are discovered, not listed (issue #35): a hand-kept list had
/// already missed DialogEditor.PatchManager's Strings.axaml, so a plural added there
/// cleared the guard. Views are out of scope here — literal text in a view, including
/// a StringFormat, is NoHardcodedUiStringsTests' job, which leaves the dictionaries
/// as the only place a "(s)" can legitimately be written.
/// </summary>
public class NoNaivePluralTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Same exclusion as the other source-tree scans: build output and sibling
    // worktrees (.claude/worktrees/<name>) are copies of some other commit.
    private static bool IsExcluded(string path, string root)
    {
        var segments = Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj")
            || segments.Contains(".worktrees") || segments.Contains("worktrees");
    }

    // A string dictionary is any .axaml that declares <sys:String x:Key=…> entries
    // (matched on ":String x:Key=" so the namespace prefix does not matter), plus every
    // .resx (the Core layer's strings). Paths are solution-relative with '/' so the
    // theory's display names are stable across machines.
    public static TheoryData<string> StringDictionaries()
    {
        var root = SolutionRoot();
        var data = new TheoryData<string>();
        var files = Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains(":String x:Key="))
            .Concat(Directory.EnumerateFiles(root, "*.resx", SearchOption.AllDirectories))
            .Where(f => !IsExcluded(f, root))
            .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(f => f, StringComparer.Ordinal);
        foreach (var f in files) data.Add(f);
        return data;
    }

    [Fact]
    public void Discovery_FindsEveryKnownStringDictionary()
    {
        // Typed lambda parameter: since xunit 2.7, TheoryData<T> enumerates both as
        // object[] rows and as typed rows, so an untyped `row` can't be inferred.
        var found = StringDictionaries().Select((object[] row) => (string)row[0]).ToList();
        Assert.Contains("DialogEditor.Avalonia/Resources/Strings.axaml", found);
        Assert.Contains("DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml", found);
        Assert.Contains("DialogEditor.PatchManager/Resources/Strings.axaml", found);
        Assert.Contains("DialogEditor.Core/Resources/Strings.resx", found);
    }

    [Theory]
    [MemberData(nameof(StringDictionaries))]
    public void StringValues_NeverUseNaivePluralSuffix(string relativePath)
    {
        var text = File.ReadAllText(Path.Combine(SolutionRoot(), relativePath));
        // .axaml: <sys:String x:Key="K">…(s)…</sys:String>
        // .resx:  <data name="K" …><value>…(s)…</value>
        var pattern = relativePath.EndsWith(".resx", StringComparison.OrdinalIgnoreCase)
            ? @"<data name=""([^""]+)""[^>]*>\s*<value>[^<]*\((?:e?s)\)"
            : @"x:Key=""([^""]+)""[^<]*\((?:e?s)\)";
        var offenders = Regex.Matches(text, pattern)
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Empty(offenders);
    }
}
