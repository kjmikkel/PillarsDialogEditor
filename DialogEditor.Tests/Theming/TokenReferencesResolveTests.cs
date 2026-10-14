using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Every <c>Brush.*</c> / <c>FontSize.*</c> token a view references must actually be defined.
///
/// Avalonia does not fail on an unknown DynamicResource key: the property just stays unset.
/// Issue #182 found a separator in BatchVoImportDialog bound to <c>Brush.Border</c>, which
/// isn't a key (the border tokens are <c>Brush.Border.Default</c>, <c>.Muted</c>, <c>.Strong</c>, …),
/// so the separator had no brush and was invisible, with no error anywhere. The other token
/// guards (NoStrayHexTests, NoStrayFontSizeTests) check that views USE tokens; this one checks
/// that the tokens they use EXIST.
/// </summary>
public class TokenReferencesResolveTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Checked relative to root, not the absolute path, so this doesn't misfire when root
    // itself lives under a "worktrees" directory (e.g. when running from inside
    // .claude/worktrees/<name>).
    private static bool IsExcluded(string path, string root)
    {
        var segments = Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj")
            || segments.Contains(".worktrees") || segments.Contains("worktrees");
    }

    private static readonly Regex TokenReference = new(
        @"\{(?:Dynamic|Static)Resource\s+(?<key>(?:Brush|FontSize)\.[\w.]+)\s*\}",
        RegexOptions.Compiled);

    private static readonly Regex KeyDefinition = new(
        @"x:Key=""(?<key>[^""]+)""",
        RegexOptions.Compiled);

    [Fact]
    public void EveryReferencedTokenIsDefined()
    {
        var root = SolutionRoot();
        var files = Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f, root))
            .ToList();

        var defined = files
            .SelectMany(f => KeyDefinition.Matches(File.ReadAllText(f)))
            .Select(m => m.Groups["key"].Value)
            .ToHashSet(StringComparer.Ordinal);
        Assert.Contains("Brush.Border.Default", defined);   // an empty set would make this test vacuous

        var offenders = new List<string>();
        foreach (var file in files)
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                foreach (Match m in TokenReference.Matches(lines[i]))
                    if (!defined.Contains(m.Groups["key"].Value))
                        offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {m.Groups["key"].Value}");
        }

        Assert.True(offenders.Count == 0,
            "These Brush.*/FontSize.* keys are referenced but defined nowhere, so the property "
            + "silently stays unset. Use an existing token (see Tokens.axaml). Offenders:\n"
            + string.Join("\n", offenders));
    }
}
