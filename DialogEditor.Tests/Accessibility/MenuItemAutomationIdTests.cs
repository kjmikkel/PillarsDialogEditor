using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Stable, non-localised identifiers on menu items
/// ([issue #15](https://github.com/kjmikkel/PillarsDialogEditor/issues/15) finding 4).
///
/// Measured on 2026-09-03 via the UIA MCP server's <c>read_tree</c>: 0 of 51 menu items
/// carried an <c>AutomationId</c>, so every menu lookup — by a screen reader's automation
/// client, by an Appium test, or by our own harness — was coupled to the item's localised
/// display text, ellipsis character included. Rewording a label or running under another
/// locale broke addressing with a "not found" that looked like a missing control.
///
/// Note the deliberate INVERSION of the <see cref="AutomationNameTests"/> rule.
/// <c>AutomationProperties.Name</c> is spoken to the user, so it MUST come from a localised
/// resource. <c>AutomationProperties.AutomationId</c> is never shown or spoken, so it must
/// NOT — a resource reference there would reintroduce exactly the locale coupling this
/// fixes. Both rules are enforced structurally so neither can drift.
/// </summary>
public class MenuItemAutomationIdTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static bool IsExcluded(string path, string root)
    {
        var segments = Path.GetRelativePath(root, path)
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return segments.Contains("bin") || segments.Contains("obj")
            || segments.Contains(".worktrees") || segments.Contains("worktrees");
    }

    /// <summary>
    /// Real menu items only. <c>&lt;…MenuItem.ItemContainerTheme&gt;</c> is property-element
    /// syntax, not a menu item, and parses with that whole string as its local name.
    ///
    /// Both type names are accepted so these rules keep applying either way — but a bare
    /// <c>MenuItem</c> is itself a defect, enforced by
    /// <see cref="EveryMenuItemIsAnAccessibleMenuItem"/>.
    /// </summary>
    private static IEnumerable<XElement> MenuItems(XDocument doc) =>
        doc.Descendants().Where(e => e.Name.LocalName is "MenuItem" or "AccessibleMenuItem");

    /// <summary>The menu bars and context menus themselves, not their items.</summary>
    private static IEnumerable<XElement> MenuContainers(XDocument doc) =>
        doc.Descendants().Where(e => e.Name.LocalName is "Menu" or "ContextMenu");

    private static IEnumerable<string> AxamlFiles(string root) =>
        Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f, root));

    /// <summary>
    /// Issue #18: Avalonia's <c>MenuItemAutomationPeer</c> implements no provider interfaces,
    /// so a plain <c>MenuItem</c> exposes <c>ScrollItem</c> only and cannot be opened or
    /// activated through UI Automation. <c>AccessibleMenuItem</c> supplies the missing
    /// <c>IInvokeProvider</c>/<c>IExpandCollapseProvider</c>, so every menu item must be one.
    ///
    /// Without this rule a newly added <c>&lt;MenuItem&gt;</c> would be silently
    /// inoperable — and worse, the other rules in this class would keep passing, since they
    /// accept either type name.
    /// </summary>
    [Fact]
    public void EveryMenuItemIsAnAccessibleMenuItem()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in AxamlFiles(root))
        {
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var el in doc.Descendants().Where(e => e.Name.LocalName == "MenuItem"))
            {
                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
                offenders.Add($"{Path.GetFileName(file)}:{line}: <MenuItem Header=\"{el.Attribute("Header")?.Value}\"> should be <ctrl:AccessibleMenuItem>");
            }
        }

        Assert.True(offenders.Count == 0,
            "A plain MenuItem cannot be opened or activated through UI Automation, because "
            + "Avalonia's MenuItemAutomationPeer implements no provider interfaces (issue #18). "
            + "Use ctrl:AccessibleMenuItem. Offenders:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void EveryMenuItemCarriesAnAutomationId()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in AxamlFiles(root))
        {
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var el in MenuItems(doc))
            {
                if (el.Attribute("AutomationProperties.AutomationId") is not null) continue;

                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
                offenders.Add($"{Path.GetFileName(file)}:{line}: <MenuItem Header=\"{el.Attribute("Header")?.Value}\"> has no AutomationProperties.AutomationId");
            }
        }

        Assert.NotEmpty(AxamlFiles(root).SelectMany(f => MenuItems(XDocument.Load(f))));

        Assert.True(offenders.Count == 0,
            "Menu items must carry a stable, non-localised AutomationId so automation and "
            + "assistive technology can address them without depending on display text "
            + "(issue #15 finding 4). Offenders:\n" + string.Join("\n", offenders));
    }

    /// <summary>
    /// Issue #15 finding 5: the app's own <c>Menu</c> element had no name and no id, so
    /// tooling could only locate it by <c>ClassName='Menu'</c> — and that scoping is what
    /// keeps the title bar's OS "System" item from being treated as a peer of
    /// File/Edit/View/Test/Help. Depending on a class name for something that load-bearing
    /// is fragile; an AutomationId is the intended handle.
    ///
    /// Context menus are covered too: they were equally anonymous, so a caller could not
    /// scope a query to one of them.
    /// </summary>
    [Fact]
    public void EveryMenuContainerCarriesAnAutomationId()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in AxamlFiles(root))
        {
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var el in MenuContainers(doc))
            {
                if (el.Attribute("AutomationProperties.AutomationId") is not null) continue;

                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
                offenders.Add($"{Path.GetFileName(file)}:{line}: <{el.Name.LocalName}> has no AutomationProperties.AutomationId");
            }
        }

        Assert.True(offenders.Count == 0,
            "A menu bar or context menu must be addressable by a stable id, so tooling and "
            + "assistive technology need not fall back to matching a framework class name "
            + "(issue #15 finding 5). Offenders:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void AutomationIdsAreLiteralsNotLocalisedResources()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in AxamlFiles(root))
        {
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var el in MenuItems(doc).Concat(MenuContainers(doc)))
            {
                var id = el.Attribute("AutomationProperties.AutomationId")?.Value;
                if (id is null) continue;                       // the test above owns that case
                if (!id.TrimStart().StartsWith('{')) continue;  // a plain literal, as required

                var line = ((IXmlLineInfo)el).HasLineInfo() ? ((IXmlLineInfo)el).LineNumber : 0;
                offenders.Add($"{Path.GetFileName(file)}:{line}: AutomationId=\"{id}\" is a markup extension");
            }
        }

        Assert.True(offenders.Count == 0,
            "An AutomationId must be a plain literal. It is never shown or spoken, so a "
            + "{DynamicResource}/{StaticResource} would make it localised and reintroduce the "
            + "very locale coupling it exists to remove — the opposite of the "
            + "AutomationProperties.Name rule. Offenders:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void AutomationIdsAreUniqueWithinEachView()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();

        foreach (var file in AxamlFiles(root))
        {
            var doc = XDocument.Load(file, LoadOptions.SetLineInfo);
            var duplicates = MenuItems(doc).Concat(MenuContainers(doc))
                .Select(e => e.Attribute("AutomationProperties.AutomationId")?.Value)
                .Where(id => !string.IsNullOrEmpty(id))
                .GroupBy(id => id!, StringComparer.Ordinal)
                .Where(g => g.Count() > 1);

            foreach (var group in duplicates)
                offenders.Add($"{Path.GetFileName(file)}: AutomationId '{group.Key}' used {group.Count()} times");
        }

        Assert.True(offenders.Count == 0,
            "AutomationIds must be unique within a view. A duplicated id cannot disambiguate "
            + "a lookup, which is the whole point of adding them. Offenders:\n"
            + string.Join("\n", offenders));
    }
}
