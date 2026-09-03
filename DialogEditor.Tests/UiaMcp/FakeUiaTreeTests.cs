using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// Guards the fixture itself. The resolver is only trustworthy if the tree it is
/// tested against reproduces the ambiguity the real app actually has, so these
/// assertions pin the fake to the 2026-09-03 audit's measured findings
/// (docs/2026-09-03-uia-addressability-audit.md).
/// </summary>
public class FakeUiaTreeTests
{
    private static List<ElementInfo> Flatten(IUiaTree tree, ElementInfo? from = null)
    {
        var node = from ?? tree.Root;
        var all = new List<ElementInfo> { node };
        foreach (var child in tree.ChildrenOf(node.Id))
            all.AddRange(Flatten(tree, child));
        return all;
    }

    [Fact]
    public void ReproducesTheSixViewboxNamedPaneChromeButtons()
    {
        var all = Flatten(FakeUiaTree.AuditSnapshot());
        Assert.Equal(6, all.Count(e => e.Name == "Avalonia.Controls.Viewbox"));
    }

    [Fact]
    public void ReproducesTheLanguageLabelAndComboBoxSharingAName()
    {
        var all = Flatten(FakeUiaTree.AuditSnapshot());
        var language = all.Where(e => e.Name == "Language:").ToList();
        Assert.Equal(2, language.Count);
        Assert.Contains(language, e => e.ControlType == "Text");
        Assert.Contains(language, e => e.ControlType == "ComboBox");
    }

    [Fact]
    public void ReproducesThirtySevenNamelessPatternlessTreeItems()
    {
        var all = Flatten(FakeUiaTree.AuditSnapshot());
        var items = all.Where(e => e.ControlType == "TreeItem").ToList();
        Assert.Equal(37, items.Count);
        Assert.All(items, i => Assert.Equal("", i.Name));
        Assert.All(items, i => Assert.Empty(i.Patterns));
    }

    [Fact]
    public void ReproducesAppMenuItemsWithNoAutomationId()
    {
        // Audit finding 4 is about the APP's menu items — 0 of 51 carry an id. The OS
        // system menu item is deliberately excluded here because it DOES carry one
        // ("Item 1"), which is finding 5: it is an indistinguishable MenuItem peer of
        // File/Edit/View/Test/Help by control type, and only its id or ancestry
        // separates it. The next test pins that half.
        var all = Flatten(FakeUiaTree.AuditSnapshot());
        var appMenuItems = all
            .Where(e => e.ControlType == "MenuItem" && e.Name != "System")
            .ToList();

        Assert.NotEmpty(appMenuItems);
        Assert.All(appMenuItems, m => Assert.Equal("", m.AutomationId));
    }

    [Fact]
    public void ReproducesTheOsSystemMenuAsAMenuItemPeer()
    {
        var all = Flatten(FakeUiaTree.AuditSnapshot());

        var system = Assert.Single(all.Where(e => e.Name == "System"));
        Assert.Equal("MenuItem", system.ControlType);
        Assert.Equal("Item 1", system.AutomationId);
    }

    [Fact]
    public void ReproducesNamedPanes()
    {
        var all = Flatten(FakeUiaTree.AuditSnapshot());
        Assert.Contains(all, e => e is { ControlType: "Pane", Name: "Conversations", AutomationId: "LeftPane" });
        Assert.Contains(all, e => e is { ControlType: "Pane", Name: "Node Details", AutomationId: "RightPane" });
    }
}
