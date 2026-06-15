using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Gaps.md "Accessibility — Assistive Technology &amp; Keyboard" item 5: tooltips are
/// hover-only, so keyboard and screen-reader users never see the explanatory text that
/// sighted mouse users get from ToolTip.Tip. Every *focusable* control that carries a
/// ToolTip.Tip must mirror the same resource into AutomationProperties.HelpText, which
/// screen readers announce on focus and which MainWindow's focus-hint status bar reads
/// directly (see MainWindowFocusHintTests).
///
/// Scoped to focusable element types only — a tooltip on a non-focusable wrapper
/// (TextBlock/Border used for static legend/info content) is never reachable by
/// keyboard focus, so mirroring it there would be dead weight (tracked separately as
/// Gaps.md item 12).
///
/// Mirrors AutomationNameTests' structural-contract approach (solution-wide scan
/// anchored on DialogEditor.slnx).
/// </summary>
public class AutomationHelpTextTests
{
    private static readonly HashSet<string> FocusableElementNames = new()
    {
        "Button", "ToggleButton", "CheckBox", "RadioButton", "TextBox", "ComboBox",
        "AutoCompleteBox", "NumericUpDown", "ListBox", "ListBoxItem", "MenuItem",
        "Slider", "Expander",
    };

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
    public void FocusableControlsWithTooltipsMirrorHelpText()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root)) continue;
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);

            foreach (var el in doc.Descendants().Where(e => FocusableElementNames.Contains(e.Name.LocalName)))
            {
                var tip = el.Attribute("ToolTip.Tip")?.Value;
                if (tip is null || !tip.StartsWith("{StaticResource ", StringComparison.Ordinal))
                    continue; // no tooltip, or a non-resource tooltip (none exist today)

                var help = el.Attribute("AutomationProperties.HelpText")?.Value;
                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;

                if (help is null)
                    offenders.Add($"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName}> has ToolTip.Tip={tip} but no AutomationProperties.HelpText");
                else if (help != tip)
                    offenders.Add($"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName}> AutomationProperties.HelpText={help} does not match ToolTip.Tip={tip}");
            }
        }

        Assert.True(offenders.Count == 0,
            "Focusable controls with a ToolTip.Tip must mirror the same resource into "
            + "AutomationProperties.HelpText so keyboard and screen-reader users get the "
            + "same explanation sighted mouse users get from hover. Offenders:\n"
            + string.Join("\n", offenders));
    }
}
