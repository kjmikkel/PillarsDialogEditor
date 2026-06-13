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

    private static bool IsExcluded(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}");

    private static readonly Regex NamedWhite =
        new(@"Foreground\s*=\s*""[Ww]hite""", RegexOptions.Compiled);

    [Fact]
    public void NoHardcodedWhiteForeground()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file)) continue;
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
