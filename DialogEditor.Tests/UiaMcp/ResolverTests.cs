using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

public class ResolverTests
{
    private static Resolver NewResolver() => new(FakeUiaTree.AuditSnapshot());

    [Fact]
    public void ResolvesAnUnambiguousNameMatch()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Conversations"));

        Assert.Null(result.ErrorKind);
        Assert.Equal("Pane", result.Element!.ControlType);
    }

    [Fact]
    public void ErrorsAsAmbiguousRatherThanTakingTheFirstMatch()
    {
        // Six pane-chrome buttons share this name. DriveApp's FindFirst would have
        // silently clicked whichever came first in tree order.
        var result = NewResolver().Resolve(new Selector(Name: "Avalonia.Controls.Viewbox"));

        Assert.Equal("Ambiguous", result.ErrorKind);
        Assert.Null(result.Element);
        Assert.Contains("6", result.ErrorMessage);
    }

    [Fact]
    public void AmbiguityErrorListsTheCandidatesDistinguishingFields()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Avalonia.Controls.Viewbox"));

        Assert.Contains("PART_MenuButton", result.ErrorMessage);
        Assert.Contains("PART_CloseButton", result.ErrorMessage);
    }

    [Fact]
    public void ControlTypeDisambiguatesTheLanguageLabelFromItsComboBox()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Language:", ControlType: "ComboBox"));

        Assert.Null(result.ErrorKind);
        Assert.Equal("ComboBox", result.Element!.ControlType);
    }

    [Fact]
    public void WithinPaneScopesTheSearchToThatPanesSubtree()
    {
        var result = NewResolver().Resolve(
            new Selector(Name: "Node Details", ControlType: "TabItem", WithinPane: "Node Details"));

        Assert.Null(result.ErrorKind);
        Assert.Equal("Details", result.Element!.AutomationId);
    }

    [Fact]
    public void NthSelectsExplicitlyAmongAmbiguousMatches()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Avalonia.Controls.Viewbox", Nth: 1));

        Assert.Null(result.ErrorKind);
        Assert.Equal("PART_PinButton", result.Element!.AutomationId);
    }

    [Fact]
    public void NotFoundErrorOffersNearMisses()
    {
        // A plausible mistake: dropping the ellipsis character from a menu label.
        var result = NewResolver().Resolve(new Selector(Name: "Open Project"));

        Assert.Equal("NotFound", result.ErrorKind);
        Assert.Contains("Open Project…", result.ErrorMessage);
    }

    [Fact]
    public void NotFoundErrorNamesTheActiveUiLanguage()
    {
        // Audit finding 4: menu items have no AutomationId, so every lookup is coupled
        // to localised label text. Without the language, "not found" is ambiguous
        // between renamed, translated, and wrong-surface-open.
        var resolver = new Resolver(FakeUiaTree.AuditSnapshot(), uiLanguage: "en");

        var result = resolver.Resolve(new Selector(Name: "Definitely Not Present"));

        Assert.Equal("NotFound", result.ErrorKind);
        Assert.Contains("en", result.ErrorMessage);
    }

    [Fact]
    public void NotFoundErrorListsTheOpenPanes()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Definitely Not Present"));

        Assert.Equal("NotFound", result.ErrorKind);
        Assert.Contains("Conversations", result.ErrorMessage);
        Assert.Contains("Node Details", result.ErrorMessage);
    }

    [Fact]
    public void UnknownPaneIsReportedAsNotFoundNamingThePane()
    {
        var result = NewResolver().Resolve(new Selector(Name: "Canvas", WithinPane: "No Such Pane"));

        Assert.Equal("NotFound", result.ErrorKind);
        Assert.Contains("No Such Pane", result.ErrorMessage);
    }

    [Fact]
    public void FlattenScopedToAPaneExcludesOtherPanesContents()
    {
        var all = NewResolver().Flatten(withinPane: "Node Details");
        Assert.DoesNotContain(all, e => e.ControlType == "TreeItem");
    }
}
