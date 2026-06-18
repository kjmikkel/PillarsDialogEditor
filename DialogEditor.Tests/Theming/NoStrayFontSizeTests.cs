using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Enforces that all FontSize.* token references use <c>{DynamicResource}</c> so live font-scale
/// switching retranslates them without a restart (Gaps item 6 part B).
/// </summary>
public class NoStaticFontSizeResourceTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static bool IsExcluded(string path, string root)
    {
        var segments = Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj")
            || segments.Contains(".worktrees") || segments.Contains("worktrees");
    }

    // Resource dictionaries may use StaticResource internally for cross-references.
    private static bool IsResourceDict(string path)
    {
        var name = Path.GetFileName(path);
        return name.Contains("Tokens",   StringComparison.OrdinalIgnoreCase)
            || name.Contains("Strings",  StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Palette",StringComparison.OrdinalIgnoreCase)
            || name.Equals("App.axaml",  StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex StaticFontSizeRef =
        new(@"\{StaticResource FontSize\.\w+\}", RegexOptions.Compiled);

    [Fact]
    public void FontSizeTokensMustUseDynamicResource()
    {
        var root      = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root)) continue;
            if (IsResourceDict(file))   continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (StaticFontSizeRef.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}:  {lines[i].Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "FontSize.* tokens must use {DynamicResource} so live font-scale switching works. Offenders:\n" +
            string.Join("\n", offenders));
    }
}



/// <summary>
/// The FontSize-token contract enforcer. Bare numeric FontSize values may only appear
/// in Tokens.axaml (where the FontSize.* tokens are defined); every view, control theme,
/// and style must bind {StaticResource FontSize.*} instead. See
/// docs/superpowers/specs/2026-06-14-fontsize-token-foundation-design.md.
/// </summary>
public class NoStrayFontSizeTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Skip build output and other branches' working copies under .worktrees/ or
    // .claude/worktrees/ (gitignored, but Directory.EnumerateFiles doesn't honour
    // .gitignore). Checked relative to root, not the absolute path, so this doesn't
    // misfire when root itself lives under a "worktrees" directory (e.g. when running
    // from inside .claude/worktrees/<name>).
    private static bool IsExcluded(string path, string root)
    {
        var segments = Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj")
            || segments.Contains(".worktrees") || segments.Contains("worktrees");
    }

    // Inline attribute: FontSize="12"
    private static readonly Regex InlineFontSize = new(@"FontSize\s*=\s*""[0-9]", RegexOptions.Compiled);

    // Style setter: <Setter Property="FontSize" ... Value="12"/> — both fragments appear
    // on the same line for every existing setter, so a per-line check on each suffices.
    private static readonly Regex SetterFontSizeProperty = new(@"Property\s*=\s*""FontSize""", RegexOptions.Compiled);
    private static readonly Regex SetterNumericValue = new(@"Value\s*=\s*""[0-9]", RegexOptions.Compiled);

    [Fact]
    public void NoFontSizeLiteralsOutsideTokens()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root)) continue;
            if (Path.GetFileName(file).Equals("Tokens.axaml", StringComparison.OrdinalIgnoreCase)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var isViolation = InlineFontSize.IsMatch(line)
                    || (SetterFontSizeProperty.IsMatch(line) && SetterNumericValue.IsMatch(line));
                if (isViolation)
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
            }
        }
        Assert.True(offenders.Count == 0,
            "Bare numeric FontSize values are only allowed in Tokens.axaml; bind FontSize.* tokens instead. Offenders:\n"
            + string.Join("\n", offenders));
    }
}
