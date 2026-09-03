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
        var warnings = AddressabilityWarnings.Inspect(AuditElements());
        Assert.Contains(warnings, w => w.Kind == "NoPatterns" && w.Detail.Contains("TreeItem"));
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
