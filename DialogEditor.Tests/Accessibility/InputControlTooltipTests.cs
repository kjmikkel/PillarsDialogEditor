using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// CLAUDE.md's UI rule: every interactive control carries a ToolTip explaining its purpose.
/// Issue #182 found input fields without one (the annotation title/body boxes, the Condition
/// Editor's operator combo, the New Conversation and Language Code name boxes, the Export
/// Conversations checkboxes), and nothing caught them.
///
/// Scoped to input controls. Buttons are left out on purpose: the rule exempts self-explanatory
/// OK/Cancel/Close buttons, and those are the only tooltip-less buttons in the app; icon-only
/// buttons are covered by AutomationNameTests instead. A tooltip may be a resource reference or,
/// in a template, a binding (e.g. a parameter row's type hint), but never a literal, which
/// NoHardcodedUiStringsTests rejects anyway.
/// </summary>
public class InputControlTooltipTests
{
    private static readonly HashSet<string> InputControlNames = new()
    {
        "TextBox", "ComboBox", "AutoCompleteBox", "NumericUpDown", "CheckBox", "RadioButton", "Slider",
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
    public void InputControlsCarryTooltips()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root)) continue;
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);

            foreach (var el in doc.Descendants().Where(e => InputControlNames.Contains(e.Name.LocalName)))
            {
                if (!string.IsNullOrWhiteSpace(el.Attribute("ToolTip.Tip")?.Value)) continue;
                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
                offenders.Add($"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName}> has no ToolTip.Tip");
            }
        }

        Assert.True(offenders.Count == 0,
            "Every input control needs a ToolTip.Tip explaining what it is for (CLAUDE.md UI rule). "
            + "Bind it to a localized resource and mirror it into AutomationProperties.HelpText. Offenders:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// GitHub issue 261: ten menu items (New/Open/Save Project, Open Game Folder, Settings,
    /// Undo, Redo, Test Patch, the test restore and Restore Backup) had no tooltip while every
    /// item beside them did. A leaf item is one that runs something (Command or Click); a
    /// submenu header (has child items or an ItemsSource) is left out, as its items carry the
    /// detail.
    /// </summary>
    [Fact]
    public void LeafMenuItemsCarryTooltipAndHelpText()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file, root)) continue;
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);

            foreach (var el in doc.Descendants().Where(e => e.Name.LocalName is "MenuItem" or "AccessibleMenuItem"))
            {
                var runs = el.Attribute("Command") is not null || el.Attribute("Click") is not null;
                var hasChildren = el.Attribute("ItemsSource") is not null
                    || el.Elements().Any(c => c.Name.LocalName is "MenuItem" or "AccessibleMenuItem");
                if (!runs || hasChildren) continue;

                var tip = el.Attribute("ToolTip.Tip")?.Value;
                var help = el.Attribute("AutomationProperties.HelpText")?.Value;
                if (!string.IsNullOrWhiteSpace(tip) && !string.IsNullOrWhiteSpace(help)) continue;

                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
                offenders.Add($"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName} Header=\"{el.Attribute("Header")?.Value}\"> "
                    + "needs ToolTip.Tip and AutomationProperties.HelpText");
            }
        }

        Assert.True(offenders.Count == 0,
            "Every menu item that runs something needs a ToolTip.Tip and matching HelpText (CLAUDE.md UI rule). Offenders:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// GitHub issue 261: Dock.Avalonia's pane buttons (Pane options, Pin pane, Close pane) are
    /// in a third-party template, so App.axaml reaches them with /template/ styles. Those styles
    /// must give each button a tooltip and help text, not only an accessible name.
    /// </summary>
    [Theory]
    [InlineData("PART_MenuButton")]
    [InlineData("PART_PinButton")]
    [InlineData("PART_CloseButton")]
    public void DockPaneButtonsCarryTooltipAndHelpText(string part)
    {
        var app = XDocument.Load(Path.Combine(SolutionRoot(), "DialogEditor.Avalonia", "App.axaml"));
        var style = app.Descendants().SingleOrDefault(e => e.Name.LocalName == "Style"
            && (e.Attribute("Selector")?.Value ?? "").EndsWith("Button#" + part));
        Assert.NotNull(style);

        var setters = style!.Elements().Where(e => e.Name.LocalName == "Setter")
            .ToDictionary(e => e.Attribute("Property")!.Value, e => e.Attribute("Value")?.Value ?? "");
        Assert.Contains("{DynamicResource", setters.GetValueOrDefault("ToolTip.Tip") ?? "");
        Assert.Contains("{DynamicResource", setters.GetValueOrDefault("AutomationProperties.HelpText") ?? "");
    }
}
