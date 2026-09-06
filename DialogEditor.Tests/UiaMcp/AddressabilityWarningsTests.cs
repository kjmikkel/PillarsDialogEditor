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
        // The rule still applies; it is exercised with an explicit element because the
        // audit snapshot no longer contains an offender.
        var elements = new List<ElementInfo>
        {
            new("a", "", "Button", "PART_Mystery", "Button", true, false, true, new[] { "Invoke" }),
        };

        var unnamed = Assert.Single(AddressabilityWarnings.Inspect(elements)
            .Where(w => w.Kind == "UnnamedFocusable"));
        Assert.Contains("Button", unnamed.Detail);
    }

    [Fact]
    public void ConversationRowsNoLongerCountAsUnnamedFocusable()
    {
        // Issue #15 finding 1, name half fixed: this warning previously named 37 TreeItem
        // elements. Its absence is the evidence the fix took — inverted from asserting the
        // defect, exactly as docs/uia-fallback-inventory.md prescribes.
        var warnings = AddressabilityWarnings.Inspect(AuditElements());

        Assert.DoesNotContain(warnings,
            w => w.Kind == "UnnamedFocusable" && w.Detail.Contains("TreeItem"));
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
    public void LabelledContainersAreNotReportedAsCollisions()
    {
        // A control sharing its name with its own label is not ambiguity a caller can trip
        // over: a Text element is never an interaction target, so it cannot be the thing
        // you meant, and controlType separates them trivially. Reporting these would bury
        // the real collisions — after the finding-1 name fix there are 37 such pairs in the
        // Conversations pane alone. Same principle as the ComboBox false positive.
        var elements = new List<ElementInfo>
        {
            new("a", "09_port_maje", "TreeItem", "", "", true, false, true, new[] { "ScrollItem" }),
            new("b", "09_port_maje", "Text", "", "", true, false, false, Array.Empty<string>()),
        };

        Assert.DoesNotContain(AddressabilityWarnings.Inspect(elements), w => w.Kind == "CollidingName");
    }

    [Fact]
    public void ConversationRowsNoLongerProduceCollisionNoise()
    {
        var warnings = AddressabilityWarnings.Inspect(AuditElements());

        Assert.DoesNotContain(warnings,
            w => w.Kind == "CollidingName" && w.Detail.Contains("_conversation"));
    }

    [Fact]
    public void GenuinelyAmbiguousControlsAreStillReported()
    {
        // Two OPERABLE elements sharing a name IS ambiguous — the suppression rules must not
        // swallow the case the resolver exists for. Patterns match the live app: the dock
        // pane and its tab both expose Invoke, so "invoke name=Canvas" is genuinely unclear.
        var elements = new List<ElementInfo>
        {
            new("a", "Canvas", "Pane", "Documents", "", true, false, false, new[] { "Invoke", "ScrollItem" }),
            new("b", "Canvas", "TabItem", "Canvas", "", true, false, true, new[] { "Invoke", "SelectionItem", "ScrollItem" }),
            new("c", "Canvas", "Text", "", "", true, false, false, new[] { "ScrollItem" }),
        };

        Assert.Contains(AddressabilityWarnings.Inspect(elements),
            w => w.Kind == "CollidingName" && w.Detail.Contains("Canvas"));
    }

    [Fact]
    public void DuplicatedStaticTextIsReportedAsADuplicateLabel()
    {
        // Two Text elements with the same name is a different defect from ambiguity: a
        // screen reader announces the same thing twice. The live status bar does this.
        var elements = new List<ElementInfo>
        {
            new("a", "Opened project 'X'", "Text", "StatusLiveRegion", "", true, false, false, Array.Empty<string>()),
            new("b", "Opened project 'X'", "Text", "", "", true, false, false, Array.Empty<string>()),
        };

        var warnings = AddressabilityWarnings.Inspect(elements);

        Assert.Contains(warnings, w => w.Kind == "DuplicateLabel");
        Assert.DoesNotContain(warnings, w => w.Kind == "CollidingName");
    }

    [Fact]
    public void ChromeThatCannotBeOperatedIsNotACollisionCandidate()
    {
        // A window and its own TitleBar share the window title. The TitleBar exposes no
        // patterns and is not focusable, so it can never be the target of an action —
        // it cannot be "the one you meant". Same reasoning as stripping Text labels,
        // generalised: judge ambiguity only among things that can actually be operated.
        var elements = new List<ElementInfo>
        {
            new("a", "Pillars Dialog Editor", "Window", "", "MainWindow", true, false, false,
                new[] { "Window", "Transform" }),
            new("b", "Pillars Dialog Editor", "TitleBar", "TitleBar", "", true, false, false,
                Array.Empty<string>()),
        };

        Assert.DoesNotContain(AddressabilityWarnings.Inspect(elements), w => w.Kind == "CollidingName");
    }

    [Fact]
    public void AHiddenLiveRegionIsNotADuplicateLabel()
    {
        // MainWindow pairs a zero-size AutomationProperties.LiveSetting="Polite" TextBlock
        // with the visible status text, so a change is ANNOUNCED while the visible label is
        // what gets READ. That is a deliberate accessibility pattern, not double
        // announcement — the live region has an empty bounding rectangle, so nobody sees or
        // clicks it. UIA's LiveSetting property is not exposed by the .NET client, so size
        // is the available discriminator.
        var elements = new List<ElementInfo>
        {
            new("live", "Opened project 'X'", "Text", "StatusLiveRegion", "", true, false, false,
                new[] { "ScrollItem" }, HasSize: false),
            new("vis", "Opened project 'X'", "Text", "", "", true, false, false,
                new[] { "ScrollItem" }),
        };

        Assert.DoesNotContain(AddressabilityWarnings.Inspect(elements), w => w.Kind == "DuplicateLabel");
    }

    [Fact]
    public void ChromeSharingANameIsNotReportedAsADuplicateLabel()
    {
        // Both of these were false positives introduced while fixing the collision rule:
        // a Window with its own TitleBar, and the two splitter Thumbs both named "Position".
        // Neither is static text, so neither is announced as a duplicated label.
        var windowAndTitleBar = new List<ElementInfo>
        {
            new("a", "Pillars Dialog Editor", "Window", "", "MainWindow", true, false, false,
                new[] { "Window", "Transform" }),
            new("b", "Pillars Dialog Editor", "TitleBar", "TitleBar", "", true, false, false,
                Array.Empty<string>()),
        };
        var thumbs = new List<ElementInfo>
        {
            new("a", "Position", "Thumb", "", "", true, false, false, new[] { "ScrollItem" }),
            new("b", "Position", "Thumb", "", "", true, false, false, new[] { "ScrollItem" }),
        };

        Assert.Empty(AddressabilityWarnings.Inspect(windowAndTitleBar));
        Assert.Empty(AddressabilityWarnings.Inspect(thumbs));
    }

    [Fact]
    public void TwoVisibleLabelsWithTheSameTextAreStillReported()
    {
        var elements = new List<ElementInfo>
        {
            new("a", "Ready", "Text", "", "", true, false, false, new[] { "ScrollItem" }),
            new("b", "Ready", "Text", "", "", true, false, false, new[] { "ScrollItem" }),
        };

        Assert.Contains(AddressabilityWarnings.Inspect(elements), w => w.Kind == "DuplicateLabel");
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
