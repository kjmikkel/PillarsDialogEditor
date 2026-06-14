using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Gaps.md "Accessibility — Assistive Technology &amp; Keyboard" item 10: views hard-coded
/// <c>Foreground="White"</c> instead of binding the semantic <c>Brush.Text.OnAccent</c>
/// token — the literal can never be retinted per-theme. Solution-wide scan, mirroring
/// <see cref="NoStrayHexTests"/>, to keep named-colour foreground literals from creeping
/// back in.
/// </summary>
public class NoNamedColourForegroundTests
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

    private static readonly Regex NamedWhite =
        new(@"Foreground\s*=\s*""[Ww]hite""", RegexOptions.Compiled);

    [Fact]
    public void NoHardcodedWhiteForeground()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (NamedWhite.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }

        Assert.True(offenders.Count == 0,
            "Use {DynamicResource Brush.Text.OnAccent} instead of a hard-coded "
            + "Foreground=\"White\" literal, so High-Contrast and future palettes can "
            + "retint accent text. Offenders:\n" + string.Join("\n", offenders));
    }
}
