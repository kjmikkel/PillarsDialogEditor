using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Gaps.md "Accessibility — Assistive Technology &amp; Keyboard" item 11(b): WCAG 2.5.8
/// requires interactive targets to be at least 24x24 CSS pixels. Solution-wide scan for
/// any &lt;Button&gt; whose explicit Width or Height falls below that minimum, mirroring
/// the structural-scan pattern used by <see cref="FakeWatermarkTests"/>.
/// </summary>
public class HitTargetSizeTests
{
    private const double MinHitTarget = 24;

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

    [Fact]
    public void NoButtonExplicitlySmallerThanMinimumHitTarget()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root)) continue;
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);

            foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "Button"))
            {
                var width  = el.Attribute("Width")?.Value;
                var height = el.Attribute("Height")?.Value;

                var tooSmall =
                    (width  is not null && double.TryParse(width,  out var w) && w < MinHitTarget) ||
                    (height is not null && double.TryParse(height, out var h) && h < MinHitTarget);
                if (!tooSmall) continue;

                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
                offenders.Add($"{Path.GetFileName(file)}:{line}: <Button Width=\"{width}\" Height=\"{height}\"> "
                    + $"is below the WCAG 2.5.8 {MinHitTarget}x{MinHitTarget} minimum hit target");
            }
        }

        Assert.True(offenders.Count == 0,
            $"Interactive buttons must be at least {MinHitTarget}x{MinHitTarget} (WCAG 2.5.8). Offenders:\n"
            + string.Join("\n", offenders));
    }
}
