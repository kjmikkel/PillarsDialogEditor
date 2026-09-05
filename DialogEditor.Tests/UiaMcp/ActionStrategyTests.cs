using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// How an element gets operated. The rule the design turns on: prefer a real UIA pattern,
/// fall back to synthetic input ONLY when none exists, and always say so — a silent
/// fallback would let the app become less accessible while verification runs stayed green.
/// </summary>
public class ActionStrategyTests
{
    // patterns is a required positional array rather than a trailing `params`: a named
    // `patterns:` argument followed by unnamed ones is CS8323, and the bools need names
    // at the call sites to stay readable.
    private static ElementInfo El(string name, string controlType, string[] patterns,
        bool offscreen = false, bool focusable = true) =>
        new($"id-{name}", name, controlType, "", "", true, offscreen, focusable, patterns);

    [Fact]
    public void ActivatePrefersInvokeWhenAvailable()
    {
        var plan = ActionStrategy.Plan(El("Save node", "Button", ["Invoke", "ScrollItem"]),
            ActionKind.Activate);

        Assert.Equal("Pattern", plan.Route);
        Assert.Equal("Invoke", plan.Pattern);
        Assert.Null(plan.Warning);
    }

    [Fact]
    public void ActivateUsesToggleForAToggleButton()
    {
        var plan = ActionStrategy.Plan(El("Pin", "Button", ["Toggle", "ScrollItem"]),
            ActionKind.Activate);

        Assert.Equal("Pattern", plan.Route);
        Assert.Equal("Toggle", plan.Pattern);
    }

    [Fact]
    public void ActivatePrefersInvokeOverSelectionItemOnATabItem()
    {
        // Measured live: TabItem exposes Invoke AND SelectionItem. Invoke is the action the
        // user means by "activate"; SelectionItem alone would only change selection state.
        var plan = ActionStrategy.Plan(
            El("Node Details", "TabItem", ["Invoke", "SelectionItem", "ScrollItem"]),
            ActionKind.Activate);

        Assert.Equal("Invoke", plan.Pattern);
    }

    [Fact]
    public void ActivateFallsBackToSelectionItemWhenThereIsNoInvoke()
    {
        var plan = ActionStrategy.Plan(El("Row", "ListItem", ["SelectionItem"]),
            ActionKind.Activate);

        Assert.Equal("Pattern", plan.Route);
        Assert.Equal("SelectionItem", plan.Pattern);
    }

    [Fact]
    public void ActivateFallsBackToASyntheticClickWithAWarningForTheAppsMenuItems()
    {
        // Audit finding: the app's top-level MenuItems expose ScrollItem only.
        var plan = ActionStrategy.Plan(El("File", "MenuItem", ["ScrollItem"]),
            ActionKind.Activate);

        Assert.Equal("SyntheticClick", plan.Route);
        Assert.NotNull(plan.Warning);
        Assert.Contains("#15", plan.Warning);
        Assert.Contains("MenuItem", plan.Warning);
    }

    [Fact]
    public void ActivateFallsBackToASyntheticClickForConversationRows()
    {
        // Issue #15 finding 1: Scroll/ScrollItem only — no SelectionItem, no ExpandCollapse.
        var plan = ActionStrategy.Plan(El("", "TreeItem", ["Scroll", "ScrollItem"]),
            ActionKind.Activate);

        Assert.Equal("SyntheticClick", plan.Route);
        Assert.Contains("#15", plan.Warning);
    }

    [Fact]
    public void ExpandUsesExpandCollapseWhenPresent()
    {
        var plan = ActionStrategy.Plan(
            El("Language:", "ComboBox", ["ExpandCollapse", "Value"]), ActionKind.Expand);

        Assert.Equal("Pattern", plan.Route);
        Assert.Equal("ExpandCollapse", plan.Pattern);
    }

    [Fact]
    public void ExpandFallsBackToASyntheticClickWithAWarning()
    {
        var plan = ActionStrategy.Plan(El("File", "MenuItem", ["ScrollItem"]),
            ActionKind.Expand);

        Assert.Equal("SyntheticClick", plan.Route);
        Assert.Contains("ExpandCollapse", plan.Warning);
    }

    [Fact]
    public void SetValueUsesTheValuePattern()
    {
        var plan = ActionStrategy.Plan(El("Speaker", "Edit", ["Value", "ScrollItem"]),
            ActionKind.SetValue);

        Assert.Equal("Pattern", plan.Route);
        Assert.Equal("Value", plan.Pattern);
    }

    [Fact]
    public void SetValueFallsBackToFocusAndTypeWithAWarning()
    {
        var plan = ActionStrategy.Plan(El("Odd field", "Custom", ["ScrollItem"]),
            ActionKind.SetValue);

        Assert.Equal("FocusAndType", plan.Route);
        Assert.NotNull(plan.Warning);
    }

    [Fact]
    public void FocusNeedsOnlyKeyboardFocusability()
    {
        var plan = ActionStrategy.Plan(El("Speaker", "Edit", ["Value"]), ActionKind.Focus);

        Assert.Equal("Focus", plan.Route);
        Assert.Null(plan.Warning);
    }

    [Fact]
    public void FocusIsNotOperableOnANonFocusableElement()
    {
        var plan = ActionStrategy.Plan(El("Label", "Text", ["ScrollItem"], focusable: false),
            ActionKind.Focus);

        Assert.Equal("NotOperable", plan.Route);
        Assert.Contains("not keyboard focusable", plan.Reason);
    }

    [Fact]
    public void AnOffscreenElementWithScrollItemIsScrolledIntoViewFirst()
    {
        // The common case: most conversation rows are scrolled out of view at any moment.
        var plan = ActionStrategy.Plan(
            El("", "TreeItem", ["Scroll", "ScrollItem"], offscreen: true),
            ActionKind.Activate);

        Assert.True(plan.NeedsScrollIntoView);
        Assert.Equal("SyntheticClick", plan.Route);
    }

    [Fact]
    public void AnOffscreenElementWithoutScrollItemIsNotOperable()
    {
        var plan = ActionStrategy.Plan(
            El("Stranded", "Button", ["Invoke"], offscreen: true), ActionKind.Activate);

        Assert.Equal("NotOperable", plan.Route);
        Assert.Contains("offscreen", plan.Reason);
        Assert.Contains("ScrollItem", plan.Reason);
    }

    [Fact]
    public void AnOnscreenPatternRouteNeedsNoScrolling()
    {
        var plan = ActionStrategy.Plan(El("Save node", "Button", ["Invoke"]),
            ActionKind.Activate);

        Assert.False(plan.NeedsScrollIntoView);
    }

    [Fact]
    public void AnElementWithNoPatternsAtAllAndNoFocusIsNotOperable()
    {
        var plan = ActionStrategy.Plan(El("System Menu Bar", "MenuBar", [], focusable: false),
            ActionKind.Activate);

        Assert.Equal("NotOperable", plan.Route);
    }
}
