using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

public class AddressabilityWarningsTests
{
    private static IReadOnlyList<ElementInfo> AuditElements() =>
        new Resolver(FakeUiaTree.AuditSnapshot()).Flatten();

    [Fact]
    public void FlagsCollidingNamesWithinTheScope()
    {
        var warnings = AddressabilityWarnings.Inspect(AuditElements());

        var collision = Assert.Single(warnings.Where(
            w => w.Kind == "CollidingName" && w.Detail.Contains("Avalonia.Controls.Viewbox")));
        Assert.Contains("6", collision.Detail);
    }

    [Fact]
    public void FlagsTypeNameLeaksInAccessibleNames()
    {
        var warnings = AddressabilityWarnings.Inspect(AuditElements());
        Assert.Contains(warnings, w => w.Kind == "TypeNameLeak");
    }

    [Fact]
    public void FlagsFocusableElementsThatHaveNoName()
    {
        var warnings = AddressabilityWarnings.Inspect(AuditElements());

        var unnamed = Assert.Single(warnings.Where(
            w => w.Kind == "UnnamedFocusable" && w.Detail.Contains("TreeItem")));
        Assert.Contains("37", unnamed.Detail);
    }

    [Fact]
    public void FlagsFocusableElementsExposingNoPatterns()
    {
        // Fires on the OS System Menu Bar, which is what it fires on live. Notably NOT
        // on the conversation TreeItems: they expose Scroll/ScrollItem, so the gap there
        // is a missing SelectionItem/ExpandCollapse rather than a total absence.
        var warnings = AddressabilityWarnings.Inspect(AuditElements());
        Assert.Contains(warnings, w => w.Kind == "NoPatterns" && w.Detail.Contains("MenuBar"));
        Assert.DoesNotContain(warnings, w => w.Kind == "NoPatterns" && w.Detail.Contains("TreeItem"));
    }

    [Fact]
    public void ReturnsNothingForACleanTree()
    {
        var clean = new List<ElementInfo>
        {
            new("a", "Save", "Button", "SaveButton", "Button", true, false, true, new[] { "Invoke" }),
            new("b", "Open", "Button", "OpenButton", "Button", true, false, true, new[] { "Invoke" }),
        };

        Assert.Empty(AddressabilityWarnings.Inspect(clean));
    }
}
