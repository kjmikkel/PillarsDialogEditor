using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Gaps.md "Accessibility — Assistive Technology &amp; Keyboard" item 9: a few search/filter
/// boxes simulate a placeholder with an overlay TextBlock shown via
/// <c>IsVisible="{Binding ..., Converter={StaticResource StringIsEmpty}}"</c> instead of using
/// TextBox's real <c>Watermark</c> property. The overlay isn't exposed to the accessibility
/// tree and doesn't participate in focus/IME the way <c>Watermark</c> does, so it's a
/// fake — solution-wide scan to keep it from creeping back in.
/// </summary>
public class FakeWatermarkTests
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

    [Fact]
    public void NoOverlayTextBlocksSimulateWatermarks()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root)) continue;
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);

            foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "TextBlock"))
            {
                var isVisible = el.Attribute("IsVisible")?.Value;
                if (isVisible is null || !isVisible.Contains("StringIsEmpty"))
                    continue;

                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
                offenders.Add($"{Path.GetFileName(file)}:{line}: <TextBlock> simulates a placeholder via "
                    + $"IsVisible={isVisible} — use the sibling TextBox's Watermark property instead");
            }
        }

        Assert.True(offenders.Count == 0,
            "TextBlock overlays must not simulate placeholders — use TextBox.Watermark, which is "
            + "exposed to the accessibility tree and participates in focus/IME. Offenders:\n"
            + string.Join("\n", offenders));
    }
}
