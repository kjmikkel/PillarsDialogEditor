using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Screen-reader name enforcer (Gaps.md "Accessibility — Assistive Technology &amp;
/// Keyboard" item 1). Avalonia exposes a button's Content as its accessible name and
/// does NOT fall back to ToolTip.Tip, so an icon-only button (⚙, 📌, ⊕, ⌂, ✕, …) is
/// announced as just its glyph — meaningless through a screen reader, no matter how
/// good its tooltip is. Every Button/ToggleButton whose Content is a literal glyph
/// (no letters or digits) must therefore carry AutomationProperties.Name.
///
/// The name is user-visible text (it is spoken to the user), so per the localisation
/// rule it must come from a resource ({StaticResource}/{DynamicResource}) rather than
/// be hard-coded inline — the test asserts that provenance too, so a violation cannot
/// be "fixed" with an untranslatable English literal.
///
/// Mirrors NoStrayHexTests' structural-contract approach (solution-wide scan anchored
/// on DialogEditor.slnx): the build fails the moment a new icon-only control ships
/// without an accessible name.
/// </summary>
public class AutomationNameTests
{
    // Walk up from the test bin dir until we find the solution file, then scan every
    // project beneath it — the contract holds app-wide (editor + PatchManager), same
    // anchoring as NoStrayHexTests.
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Skip build output and leftover worktree checkouts — neither is source we own here.
    private static bool IsExcluded(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}");

    private static bool IsLocalizedResourceReference(string? value) =>
        value is not null &&
        (value.StartsWith("{StaticResource ", StringComparison.Ordinal) ||
         value.StartsWith("{DynamicResource ", StringComparison.Ordinal));

    // Template-generated fields (script/condition parameter editors) have per-row labels
    // that only exist as data, so a binding is the correct accessible name there. A
    // hard-coded literal is never acceptable — it would bypass the localisation rule.
    private static bool IsResourceReferenceOrBinding(string? value) =>
        IsLocalizedResourceReference(value) ||
        (value is not null &&
         (value.StartsWith("{Binding ", StringComparison.Ordinal) ||
          value.StartsWith("{CompiledBinding ", StringComparison.Ordinal)));

    [Fact]
    public void IconOnlyButtonsCarryLocalizedAutomationName()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file)) continue;
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);

            foreach (var el in doc.Descendants()
                         .Where(e => e.Name.LocalName is "Button" or "ToggleButton"))
            {
                var content = el.Attribute("Content")?.Value;
                if (content is null) continue;                   // content supplied elsewhere (child element / template)
                if (content.StartsWith('{')) continue;           // bound or localized text, not a literal glyph
                if (content.Any(char.IsLetterOrDigit)) continue; // real text — Content itself is the accessible name

                var name = el.Attribute("AutomationProperties.Name")?.Value;
                if (IsLocalizedResourceReference(name)) continue;

                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
                offenders.Add(name is null
                    ? $"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName} Content=\"{content}\"> has no AutomationProperties.Name"
                    : $"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName} Content=\"{content}\"> has a hard-coded AutomationProperties.Name (must be a {{StaticResource}}/{{DynamicResource}} reference per the localisation rule)");
            }
        }

        Assert.True(offenders.Count == 0,
            "Icon-only buttons are meaningless to screen readers without an accessible name. "
            + "Add AutomationProperties.Name bound to a localized resource. Offenders:\n"
            + string.Join("\n", offenders));
    }

    /// <summary>
    /// Gaps.md accessibility item 2: form fields are labelled by *adjacent* TextBlocks
    /// with no programmatic association, so every TextBox/ComboBox/AutoCompleteBox/
    /// NumericUpDown reads as an unlabeled "edit" to a screen reader. Each must carry
    /// either AutomationProperties.Name (a localized resource reference for static
    /// forms, or a binding for template-generated per-row fields) or
    /// AutomationProperties.LabeledBy pointing at its label element.
    /// </summary>
    [Fact]
    public void InputControlsCarryAccessibleLabels()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file)) continue;
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);

            foreach (var el in doc.Descendants().Where(e => e.Name.LocalName
                         is "TextBox" or "ComboBox" or "AutoCompleteBox" or "NumericUpDown"))
            {
                if (el.Attribute("AutomationProperties.LabeledBy") is not null) continue;
                var name = el.Attribute("AutomationProperties.Name")?.Value;
                if (IsResourceReferenceOrBinding(name)) continue;

                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
                offenders.Add(name is null
                    ? $"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName}> has no AutomationProperties.Name or .LabeledBy"
                    : $"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName}> has a hard-coded AutomationProperties.Name (must be a resource reference or binding per the localisation rule)");
            }
        }

        Assert.True(offenders.Count == 0,
            "Input controls read as unlabeled \"edit\" fields to screen readers without an accessible name. "
            + "Add AutomationProperties.Name (localized resource, or a binding in templates) or AutomationProperties.LabeledBy. Offenders:\n"
            + string.Join("\n", offenders));
    }
}
