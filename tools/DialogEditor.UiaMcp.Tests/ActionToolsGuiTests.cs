using System.IO;   // not in the WPF implicit-usings set
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp.Tests;

/// <summary>
/// Live-app coverage for the acting layer. These pin the behaviours that only appear against
/// a real window: pattern routes actually working, the ambiguity refusal, and the
/// synthetic-click fallback being reported.
///
/// Several tests deliberately assert that TODAY'S DEFECTS STILL EXIST. When issue #15 fixes
/// them these tests fail, and that failure is the documented signal to delete the matching
/// fallback — see docs/uia-fallback-inventory.md.
/// </summary>
[Trait("Category", "Gui")]
public class ActionToolsGuiTests(EditorSessionFixture fixture) : IClassFixture<EditorSessionFixture>
{
    private ElementInfo Resolve(Selector selector)
    {
        var result = new Resolver(fixture.Session.Tree()).Resolve(selector);
        Assert.Null(result.ErrorKind);
        return result.Element!;
    }

    [Fact]
    public void TheAppMenuIsFoundAndExcludesTheOsSystemItem()
    {
        var all = new Resolver(fixture.Session.Tree()).Flatten();
        var appMenu = all.Single(e => e.ClassName == "Menu" && e.ControlType == "Menu");

        var tops = fixture.Session.Tree().ChildrenOf(appMenu.Id)
            .Where(c => c.ControlType == "MenuItem").Select(c => c.Name).ToList();

        Assert.Equal(new[] { "File", "Edit", "View", "Test", "Help" }, tops);
        Assert.DoesNotContain("System", tops);
    }

    [Fact]
    public void MenuItemsExposeStableAutomationIdsToUia()
    {
        // Issue #15 finding 4, fixed. MenuItemAutomationIdTests enforces the markup; this
        // pins that the ids actually REACH the UIA tree, which markup alone cannot prove.
        var all = new Resolver(fixture.Session.Tree()).Flatten();
        var appMenu = all.Single(e => e.ClassName == "Menu" && e.ControlType == "Menu");
        var tops = fixture.Session.Tree().ChildrenOf(appMenu.Id)
            .Where(c => c.ControlType == "MenuItem").ToList();

        Assert.Equal(
            new[] { "MenuFile", "MenuEdit", "MenuView", "MenuTest", "MenuHelp" },
            tops.Select(t => t.AutomationId));

        // Ids must be independent of the display text, which is localised.
        Assert.All(tops, t => Assert.NotEqual(t.Name, t.AutomationId));
    }

    [Fact]
    public void AmbiguousNamesAreRefusedRatherThanGuessed()
    {
        var result = new Resolver(fixture.Session.Tree())
            .Resolve(new Selector(Name: "Avalonia.Controls.Viewbox"));

        Assert.Equal("Ambiguous", result.ErrorKind);
        Assert.Contains("nth=0", result.ErrorMessage);
    }

    [Fact]
    public void TopLevelMenuItemsStillLackInvokeAndExpandCollapse()
    {
        // Pins the claim in DriveApp.ps1's header. When issue #15 fixes this, THIS TEST
        // SHOULD FAIL — that is the signal to delete the synthetic-click fallback.
        var file = Resolve(new Selector(Name: "File", ControlType: "MenuItem"));

        Assert.DoesNotContain("Invoke", file.Patterns);
        Assert.DoesNotContain("ExpandCollapse", file.Patterns);

        var plan = ActionStrategy.Plan(file, ActionKind.Activate);
        Assert.Equal("SyntheticClick", plan.Route);
        Assert.Contains("#15", plan.Warning);
    }

    [Fact]
    public void ConversationRowsAreReachableByName()
    {
        // Issue #15 finding 1, name half. The rows used to have an empty Name with the
        // label sitting on a child Text element, so a lookup for a conversation returned
        // something unselectable — and a screen reader announced nothing at all.
        var all = new Resolver(fixture.Session.Tree()).Flatten();
        var rows = all.Where(e => e.ControlType == "TreeItem").ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.False(string.IsNullOrWhiteSpace(r.Name),
            "a conversation row has no accessible name"));

        // The name must be the row's own label, not a shared placeholder — otherwise the
        // rows would all collide and still not be individually addressable.
        var distinct = rows.Select(r => r.Name).Distinct(StringComparer.Ordinal).Count();
        Assert.Equal(rows.Count, distinct);
    }

    [Fact]
    public void ConversationRowsAreSelectableAndExpandableByPattern()
    {
        // Issue #15 finding 1, fixed. Inverted from asserting the defect — the previous form
        // failing is what signalled the fix had landed, exactly as the fallback inventory
        // prescribes. Supplied by ConversationTreeViewItemAutomationPeer, since Avalonia's
        // own TreeViewItem peer implements no provider interfaces.
        var rows = new Resolver(fixture.Session.Tree()).Flatten()
            .Where(e => e.ControlType == "TreeItem").ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Contains("SelectionItem", r.Patterns));
        Assert.All(rows, r => Assert.Contains("ExpandCollapse", r.Patterns));
        Assert.All(rows, r => Assert.Contains("ScrollItem", r.Patterns));
    }

    [Fact]
    public void ActivatingAConversationRowUsesAPatternNotASyntheticClick()
    {
        var row = new Resolver(fixture.Session.Tree()).Flatten()
            .First(e => e.ControlType == "TreeItem");

        var plan = ActionStrategy.Plan(row, ActionKind.Activate);

        Assert.Equal("Pattern", plan.Route);
        Assert.Equal("SelectionItem", plan.Pattern);
        Assert.Null(plan.Warning);
    }

    [Fact]
    public void AComboBoxIsOperatedByPatternWithoutAWarning()
    {
        // Guards against the false positive found during phase 3: a ComboBox exposes
        // ExpandCollapse and no Invoke, and warning about that would blame the app for
        // correct behaviour.
        var combo = Resolve(new Selector(Name: "Language:", ControlType: "ComboBox"));

        var plan = ActionStrategy.Plan(combo, ActionKind.Activate);
        Assert.Equal("Pattern", plan.Route);
        Assert.Equal("ExpandCollapse", plan.Pattern);
        Assert.Null(plan.Warning);
    }

    [Fact]
    public void TextFieldsAreSetByTheValuePattern()
    {
        var filter = Resolve(new Selector(Name: "Filter conversations…", ControlType: "Edit"));

        var plan = ActionStrategy.Plan(filter, ActionKind.SetValue);
        Assert.Equal("Pattern", plan.Route);
        Assert.Equal("Value", plan.Pattern);
    }

    [Fact]
    public void TheFixtureDoesNotUseTheProductionSettingsBackupPath()
    {
        // Guards the fixture's own safety property rather than the app's behaviour.
        Assert.False(
            File.Exists(Path.Combine(Path.GetTempPath(), "PillarsDialogEditor.settings.backup.json")),
            "the Gui fixture must not use the production backup path");
    }
}
