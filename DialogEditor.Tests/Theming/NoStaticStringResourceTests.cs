using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Enforces that all string resources use <c>{DynamicResource}</c> (not <c>{StaticResource}</c>)
/// in view files so live language switching retranslates them without a restart.
///
/// Identification heuristic: string keys always use underscore separators (e.g.
/// <c>Status_OpenFolder</c>, <c>Settings_Theme</c>); converter and style keys never do
/// (e.g. <c>FontScaleToPercent</c>, <c>ToolbarPlainButton</c>). <c>{StaticResource FontSize.*}</c>
/// and <c>{StaticResource Palette.*}</c> use dots — excluded by the same rule.
///
/// Resource dictionary files themselves (<c>Strings.axaml</c>, <c>Tokens.axaml</c>, etc.)
/// are excluded — they may reference each other with <c>{StaticResource}</c> internally.
///
/// Exception: <c>StringFormat={StaticResource ...}</c> is allowed. Avalonia's Binding
/// <c>StringFormat</c> parameter only accepts literal strings (not markup extensions), so
/// format-string resources must remain <c>{StaticResource}</c>.
/// </summary>
public class NoStaticStringResourceTests
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

    // Sibling worktrees share the same solution root but are independent branches;
    // violations there are the responsibility of those branches.
    private static bool IsWorktree(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}worktrees{Path.DirectorySeparatorChar}");

    // Resource dict files are allowed to use StaticResource for internal cross-references.
    private static bool IsResourceDict(string path)
    {
        var name = Path.GetFileName(path);
        return name.Equals("App.axaml",          StringComparison.OrdinalIgnoreCase)
            || name.Contains("Strings",           StringComparison.OrdinalIgnoreCase)
            || name.Contains("Tokens",            StringComparison.OrdinalIgnoreCase)
            || name.StartsWith("Palette",         StringComparison.OrdinalIgnoreCase);
    }

    // Match {StaticResource Key_With_Underscore} — string keys only.
    // Negative lookbehind for "StringFormat=" because Binding.StringFormat only accepts a
    // literal string, not a markup extension — those uses must stay {StaticResource}.
    private static readonly Regex StaticStringRef =
        new(@"(?<!StringFormat=)\{StaticResource \w+_[\w_]+\}", RegexOptions.Compiled);

    [Fact]
    public void StringResourcesMustUseDynamicResource()
    {
        var root      = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsBuildArtifact(file)) continue;
            if (IsWorktree(file))      continue;
            if (IsResourceDict(file))  continue;

            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (StaticStringRef.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetRelativePath(root, file)}:{i + 1}:  {lines[i].Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "String resources must use {DynamicResource} so live language switching works. " +
            "FontSize.* may stay {StaticResource} (restart-required by design). Offenders:\n" +
            string.Join("\n", offenders));
    }
}
