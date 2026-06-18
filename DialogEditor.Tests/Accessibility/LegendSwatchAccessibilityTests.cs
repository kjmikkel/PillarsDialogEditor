using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Gaps.md item 12: the 7 legend swatches in LegendWindow's "Connections" and "Node
/// Types" sections are wrapped in focusable Button.legendRow elements so keyboard and
/// screen-reader users can reach the same explanations sighted mouse users get from
/// hover. Pins the structural contract: exactly 7 such buttons, each with
/// AutomationProperties.Name / ToolTip.Tip / AutomationProperties.HelpText all set to
/// the same {StaticResource ...} reference, and each still wrapping a Border +
/// TextBlock so a future edit can't silently drop the visible swatch/label while
/// keeping the wrapper.
///
/// Mirrors AutomationHelpTextTests' structural-contract approach, but scoped to a
/// single known file rather than a solution-wide scan.
/// </summary>
public class LegendSwatchAccessibilityTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void LegendRows_AreFocusableButtonsWithMirroredAccessibilityText()
    {
        var path = Path.Combine(SolutionRoot(), "DialogEditor.Avalonia", "Views", "LegendWindow.axaml");
        var doc = XDocument.Load(path, LoadOptions.SetLineInfo);

        var rows = doc.Descendants()
            .Where(e => e.Name.LocalName == "Button"
                        && (e.Attribute("Classes")?.Value ?? "").Split(' ').Contains("legendRow"))
            .ToList();

        Assert.Equal(7, rows.Count);

        foreach (var row in rows)
        {
            var line = ((IXmlLineInfo)row).HasLineInfo() ? ((IXmlLineInfo)row).LineNumber : 0;

            var name = row.Attribute("AutomationProperties.Name")?.Value;
            var tip = row.Attribute("ToolTip.Tip")?.Value;
            var help = row.Attribute("AutomationProperties.HelpText")?.Value;

            Assert.True(name is not null
                    && (name.StartsWith("{StaticResource ", StringComparison.Ordinal)
                        || name.StartsWith("{DynamicResource ", StringComparison.Ordinal)),
                $"LegendWindow.axaml:{line}: legendRow Button must have AutomationProperties.Name set to a resource reference ({{StaticResource ...}} or {{DynamicResource ...}})");
            Assert.Equal(name, tip);
            Assert.Equal(name, help);

            Assert.True(row.Descendants().Any(e => e.Name.LocalName == "Border"),
                $"LegendWindow.axaml:{line}: legendRow Button must still contain a Border (the colour/shape swatch)");
            Assert.True(row.Descendants().Any(e => e.Name.LocalName == "TextBlock"),
                $"LegendWindow.axaml:{line}: legendRow Button must still contain a TextBlock (the label)");
        }
    }
}
