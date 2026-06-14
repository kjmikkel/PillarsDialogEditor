using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Theming;

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

    // Skip build output and other branches' working copies under .worktrees/ (gitignored,
    // but Directory.EnumerateFiles doesn't honour .gitignore).
    private static bool IsExcluded(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}");

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
            if (IsExcluded(file)) continue;
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
