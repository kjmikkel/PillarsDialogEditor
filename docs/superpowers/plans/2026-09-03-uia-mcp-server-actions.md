# UIA MCP Server — Actions Implementation Plan (Phase 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let the MCP server *operate* the Dialog Editor — click, type, set values, focus, screenshot, invoke menu commands — preferring real UI Automation patterns and reporting every synthetic-input fallback as the addressability defect it is.

**Architecture:** The decision of *how* to operate an element is extracted into `ActionStrategy`, a pure function over `ElementInfo` living in `DialogEditor.UiaMcp.Core`. It is unit-tested against the audit-seeded fake tree with no GUI. The server assembly only executes the plan it is handed. A second, `net8.0-windows` test project finally gives the live layer automated coverage behind a `Gui` trait.

**Tech Stack:** .NET 8, `ModelContextProtocol` 2.2.0, `Microsoft.Extensions.Hosting` 10.0.11, xunit 2.5.3, UI Automation client (`UIAutomationClient` / `UIAutomationTypes` via `UseWPF`), `System.Windows.Forms.SendKeys`, `System.Drawing` for screenshots.

**Spec:** `docs/superpowers/specs/2026-09-03-uia-mcp-server-design.md`

**Predecessor:** `docs/superpowers/plans/2026-09-03-uia-mcp-server-foundation.md` (phases 1–2, complete). That plan built `SettingsGuard`, `BuildOutputParser`, `TestOutputParser`, `IUiaTree`/`ElementInfo`, `FakeUiaTree`, `Selector`, `RefTable`, `Resolver`, `AddressabilityWarnings`, the MCP host, `EditorSession`, `UiaTree`, and the tools `launch_app`, `session_status`, `kill_app`, `read_tree`, `find`, `menu`, `window_title`, `read_status_bar`, `build`, `run_tests`.

## Measured facts this plan is built on

Taken from `read_tree` against the live app on 2026-09-03, **not** from the original audit
(whose pattern claims were under-reported by a probe bug — see the correction in
`docs/2026-09-03-uia-addressability-audit.md`). Use these exact strings; they are what
`UiaTree` produces after normalising `ProgrammaticName`.

| ControlType | Patterns exposed | Consequence |
|---|---|---|
| `Button` | `Invoke`, sometimes `Toggle` | operable by pattern |
| `Edit` | `Value` | `set_value` uses `ValuePattern` |
| `ComboBox` | `Selection`, `Value`, `Scroll`, `ExpandCollapse` | operable by pattern |
| `TabItem` | `Invoke`, `SelectionItem` | operable by pattern |
| `MenuItem` (app's own) | **`ScrollItem` only** | no `Invoke`, no `ExpandCollapse` → synthetic click |
| `MenuItem` (`System`, `id='Item 1'`) | `ExpandCollapse` | OS chrome; never operate it |
| `TreeItem` (conversation rows) | `Scroll`, `ScrollItem` | no `SelectionItem`/`ExpandCollapse` → synthetic click (issue #15 finding 1) |
| `ScrollBar` | `RangeValue` | |
| `MenuBar`, `TitleBar` | none | not operable |
| almost everything | `ScrollItem` | scroll-into-view is broadly available |

Two claims to leave alone, both verified correct:

- `DriveApp.ps1`'s header is right that Avalonia's **top-level menu items support neither
  `Invoke` nor `ExpandCollapse`**. The `ExpandCollapse` seen on a `MenuItem` belongs to the
  OS `System` item, not to File/Edit/View/Test/Help. Do not "fix" that comment.
- The conversation `TreeItem`s do expose `Scroll`/`ScrollItem`. They lack
  `SelectionItem`/`ExpandCollapse`, which is the actual defect.

## Global Constraints

- **TDD is mandatory.** A failing test precedes every piece of non-trivial logic.
- **No bare `catch { }` in production code.** Log caught exceptions; `OperationCanceledException` is the sole silent exception. The server projects do not reference `DialogEditor.Core`, so `AppLog` is unavailable there — write to **stderr**, which is this process's log channel.
- **stdout is the JSON-RPC channel.** Never `Console.WriteLine` in the server.
- **No in-app component.** Nothing in this plan modifies `DialogEditor.Avalonia`. No remote-control endpoint, network test hook, or automation backdoor.
- **Every synthetic-input fallback must be reported in the tool result**, naming issue #15. A silent fallback is the failure mode this whole design exists to prevent.
- **Every fallback carries its issue reference in a code comment**, so Phase 4 can find and delete it when the corresponding #15 phase lands.
- `DialogEditor.UiaMcp.Core` targets `net8.0` and must never reference a UIA assembly.
- Package versions pinned: `ModelContextProtocol` `2.2.0`, `Microsoft.Extensions.Hosting` `10.0.11`, xunit `2.5.3`.
- Existing test count baseline: **2349 passing**, of which 46 are `DialogEditor.Tests.UiaMcp`. Any drop is a regression.

---

### Task 1: Fixture fidelity + `ActionStrategy`

The heart of the phase. `ActionStrategy` decides how to operate an element; the server just
executes. Because it is a pure function over `ElementInfo`, all of the interesting behaviour
is testable with no GUI.

**Files:**
- Modify: `DialogEditor.Tests/UiaMcp/FakeUiaTree.cs` (real pattern sets)
- Create: `tools/DialogEditor.UiaMcp.Core/ActionStrategy.cs`
- Test: `DialogEditor.Tests/UiaMcp/ActionStrategyTests.cs`

**Interfaces:**
- Consumes: `ElementInfo` (phase 1–2).
- Produces: `enum ActionKind { Activate, Expand, SetValue, Focus }`; `record ActionPlan(string Route, string? Pattern, bool NeedsScrollIntoView, string? Warning, string? Reason)`; `static ActionPlan ActionStrategy.Plan(ElementInfo element, ActionKind action)`. `Route` is one of `"Pattern"`, `"SyntheticClick"`, `"FocusAndType"`, `"Focus"`, `"NotOperable"`.

- [x] **Step 1: Give the fake the pattern sets measured on the live app**

In `DialogEditor.Tests/UiaMcp/FakeUiaTree.cs`, inside `AuditSnapshot()`:

Replace the menu construction block with one that carries the real patterns — app menu items
get `ScrollItem` only, the OS `System` item gets `ExpandCollapse`:

```csharp
        // Measured live: the OS system menu item DOES expose ExpandCollapse; the app's own
        // top-level items expose ScrollItem only — no Invoke, no ExpandCollapse.
        t.Add(sysBar.Id, "System", "MenuItem", "Item 1", focusable: true,
            patterns: new[] { "ExpandCollapse" });

        // The app's own menu is ANONYMOUS — selectable only by ClassName.
        var menu = t.Add(win.Id, "", "Menu", className: "Menu",
            patterns: new[] { "Scroll", "ScrollItem" });
        foreach (var top in new[] { "File", "Edit", "View", "Test", "Help" })
        {
            var mi = t.Add(menu.Id, top, "MenuItem", className: "MenuItem",
                patterns: new[] { "ScrollItem" });
            if (top == "File")
                foreach (var item in new[] { "New Project…", "Open Project…", "Save Project", "Close Project" })
                    t.Add(mi.Id, item, "MenuItem", className: "MenuItem",
                        patterns: new[] { "ScrollItem" });
            if (top == "Edit")
            {
                t.Add(mi.Id, "↩", "MenuItem", patterns: new[] { "ScrollItem" });
                t.Add(mi.Id, "↪", "MenuItem", patterns: new[] { "ScrollItem" });
            }
        }
```

Delete the old `t.Add(sysBar.Id, "System", "MenuItem", "Item 1");` line, since the block above
replaces it.

**Do not add other elements to the fake in this task.** `ActionStrategyTests` below builds
its own `ElementInfo` values, so it needs nothing from the fixture — and adding, say, an
offscreen `TreeItem` under the Node Details pane would break the existing
`ResolverTests.FlattenScopedToAPaneExcludesOtherPanesContents`, which asserts that pane
contains no `TreeItem`. Keep this step to the menu pattern fidelity above.

- [x] **Step 2: Write the failing tests**

`DialogEditor.Tests/UiaMcp/ActionStrategyTests.cs`:

```csharp
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
```

- [x] **Step 3: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~ActionStrategyTests"`
Expected: FAIL — `ActionStrategy` does not exist (CS0103).

- [x] **Step 4: Write the implementation**

`tools/DialogEditor.UiaMcp.Core/ActionStrategy.cs`:

```csharp
namespace DialogEditor.UiaMcp.Core;

public enum ActionKind { Activate, Expand, SetValue, Focus }

/// <summary>
/// How to operate one element. <paramref name="Route"/> is "Pattern", "SyntheticClick",
/// "FocusAndType", "Focus" or "NotOperable".
/// </summary>
public record ActionPlan(
    string Route,
    string? Pattern,
    bool NeedsScrollIntoView,
    string? Warning,
    string? Reason);

/// <summary>
/// Decides how to operate an element, preferring a real UI Automation pattern and falling
/// back to synthetic input only when none exists — always with a warning.
///
/// The warning is the point. An element that can only be reached by clicking its rectangle
/// is a defect a screen-reader user hits too, so the harness has to keep that signal
/// visible instead of quietly routing around it. Every fallback here is expected to
/// disappear as issue #15's phases land.
/// </summary>
public static class ActionStrategy
{
    private const string Issue = "issue #15";

    public static ActionPlan Plan(ElementInfo element, ActionKind action)
    {
        var scroll = element.IsOffscreen && element.Patterns.Contains("ScrollItem");

        // Offscreen with no way to bring it into view: nothing below can work, because both
        // a synthetic click and a focus need a real on-screen position.
        if (element.IsOffscreen && !element.Patterns.Contains("ScrollItem"))
        {
            return NotOperable(
                $"'{Describe(element)}' is offscreen and exposes no ScrollItem pattern, so it " +
                "cannot be brought into view. Scroll its container first, then re-run read_tree.");
        }

        return action switch
        {
            ActionKind.Focus => PlanFocus(element, scroll),
            ActionKind.Activate => PlanActivate(element, scroll),
            ActionKind.Expand => PlanExpand(element, scroll),
            ActionKind.SetValue => PlanSetValue(element, scroll),
            _ => NotOperable($"Unsupported action '{action}'."),
        };
    }

    private static ActionPlan PlanFocus(ElementInfo e, bool scroll) =>
        e.IsFocusable
            ? new ActionPlan("Focus", null, scroll, null, null)
            : NotOperable($"'{Describe(e)}' is not keyboard focusable.");

    private static ActionPlan PlanActivate(ElementInfo e, bool scroll)
    {
        // Invoke first: it is what "activate" means. SelectionItem only changes selection,
        // so it is a fallback rather than a peer.
        foreach (var p in new[] { "Invoke", "Toggle", "SelectionItem" })
            if (e.Patterns.Contains(p))
                return new ActionPlan("Pattern", p, scroll, null, null);

        return Clickable(e)
            ? new ActionPlan("SyntheticClick", null, scroll, FallbackWarning(e, "Invoke, Toggle or SelectionItem"), null)
            : NotOperable($"'{Describe(e)}' exposes no actionable pattern and is not clickable.");
    }

    private static ActionPlan PlanExpand(ElementInfo e, bool scroll)
    {
        if (e.Patterns.Contains("ExpandCollapse"))
            return new ActionPlan("Pattern", "ExpandCollapse", scroll, null, null);

        return Clickable(e)
            ? new ActionPlan("SyntheticClick", null, scroll, FallbackWarning(e, "ExpandCollapse"), null)
            : NotOperable($"'{Describe(e)}' exposes no ExpandCollapse pattern and is not clickable.");
    }

    private static ActionPlan PlanSetValue(ElementInfo e, bool scroll)
    {
        if (e.Patterns.Contains("Value"))
            return new ActionPlan("Pattern", "Value", scroll, null, null);

        return e.IsFocusable
            ? new ActionPlan("FocusAndType", null, scroll, FallbackWarning(e, "Value"), null)
            : NotOperable($"'{Describe(e)}' exposes no Value pattern and is not focusable, " +
                          "so its text cannot be set.");
    }

    // An element with no pattern is still reachable by clicking, provided it is a real
    // interactive target. Focusability is the proxy: labels and decorations are not.
    private static bool Clickable(ElementInfo e) => e.IsFocusable || e.ControlType == "MenuItem";

    private static string FallbackWarning(ElementInfo e, string expected) =>
        $"{e.ControlType} '{Describe(e)}' exposes no {expected} pattern; used synthetic input " +
        $"at its bounding rectangle instead. That is an addressability defect, not a normal " +
        $"code path — see {Issue}. Patterns present: [{string.Join(",", e.Patterns)}].";

    private static string Describe(ElementInfo e) =>
        e.Name.Length > 0 ? e.Name
        : e.AutomationId.Length > 0 ? $"<unnamed, id={e.AutomationId}>"
        : $"<unnamed {e.ControlType}>";

    private static ActionPlan NotOperable(string reason) => new("NotOperable", null, false, null, reason);
}
```

- [x] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~ActionStrategyTests"`
Expected: PASS, 16 tests.

- [x] **Step 6: Run the whole UiaMcp subset — the fixture changed, so earlier tests must still hold**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~DialogEditor.Tests.UiaMcp"`
Expected: PASS. If `FakeUiaTreeTests` or `AddressabilityWarningsTests` now fail, the fixture
edit changed counts they assert — reconcile against the measured facts table above rather
than loosening the assertion.

- [x] **Step 7: Commit**

```bash
git add tools/DialogEditor.UiaMcp.Core/ActionStrategy.cs DialogEditor.Tests/UiaMcp/ActionStrategyTests.cs DialogEditor.Tests/UiaMcp/FakeUiaTree.cs
git commit -m "feat(uiamcp): decide how to operate an element, warning on every fallback"
```

---

### Task 2: `SendKeysEscaper`

`SendKeys` treats `+ ^ % ~ ( ) { } [ ]` as syntax. The project already carries the scar:
sending a literal `[` requires `{[}`. Encode it once, with tests, instead of remembering it.

**Files:**
- Create: `tools/DialogEditor.UiaMcp.Core/SendKeysEscaper.cs`
- Test: `DialogEditor.Tests/UiaMcp/SendKeysEscaperTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `static string SendKeysEscaper.EscapeLiteral(string text)`.

- [x] **Step 1: Write the failing tests**

`DialogEditor.Tests/UiaMcp/SendKeysEscaperTests.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.Tests.UiaMcp;

/// <summary>
/// SendKeys treats + ^ % ~ ( ) { } [ ] as syntax, so literal text containing them must be
/// brace-wrapped. Getting this wrong does not error — it silently sends the WRONG KEYS,
/// which is why it is worth a unit test rather than a comment.
/// </summary>
public class SendKeysEscaperTests
{
    [Theory]
    [InlineData("[", "{[}")]
    [InlineData("]", "{]}")]
    [InlineData("+", "{+}")]
    [InlineData("^", "{^}")]
    [InlineData("%", "{%}")]
    [InlineData("~", "{~}")]
    [InlineData("(", "{(}")]
    [InlineData(")", "{)}")]
    public void EscapesEverySendKeysMetacharacter(string input, string expected)
        => Assert.Equal(expected, SendKeysEscaper.EscapeLiteral(input));

    [Fact]
    public void EscapesBracesThemselves()
    {
        Assert.Equal("{{}", SendKeysEscaper.EscapeLiteral("{"));
        Assert.Equal("{}}", SendKeysEscaper.EscapeLiteral("}"));
    }

    [Fact]
    public void LeavesOrdinaryTextAlone()
        => Assert.Equal("Eder says hello", SendKeysEscaper.EscapeLiteral("Eder says hello"));

    [Fact]
    public void EscapesMetacharactersEmbeddedInRealDialogueText()
    {
        // Dialogue text carries substitution tokens in square brackets, so this is the
        // realistic case rather than an edge case.
        Assert.Equal("Hello {[}playername{]}, 50{%} done",
            SendKeysEscaper.EscapeLiteral("Hello [playername], 50% done"));
    }

    [Fact]
    public void HandlesEmptyInput()
        => Assert.Equal("", SendKeysEscaper.EscapeLiteral(""));
}
```

- [x] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~SendKeysEscaperTests"`
Expected: FAIL — `SendKeysEscaper` does not exist.

- [x] **Step 3: Write the implementation**

`tools/DialogEditor.UiaMcp.Core/SendKeysEscaper.cs`:

```csharp
using System.Text;

namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// Escapes literal text for System.Windows.Forms.SendKeys, whose syntax claims
/// + ^ % ~ ( ) { } [ ]. An unescaped metacharacter does not raise an error — it silently
/// sends different keys than intended, so this is worth doing exactly once and testing.
/// </summary>
public static class SendKeysEscaper
{
    private const string Metacharacters = "+^%~(){}[]";

    public static string EscapeLiteral(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (Metacharacters.Contains(c)) sb.Append('{').Append(c).Append('}');
            else sb.Append(c);
        }
        return sb.ToString();
    }
}
```

- [x] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~SendKeysEscaperTests"`
Expected: PASS, 12 tests (8 theory cases + 4 facts).

- [x] **Step 5: Commit**

```bash
git add tools/DialogEditor.UiaMcp.Core/SendKeysEscaper.cs DialogEditor.Tests/UiaMcp/SendKeysEscaperTests.cs
git commit -m "feat(uiamcp): escape literal text for SendKeys"
```

---

### Task 3: `ElementOperator` — executing a plan against live UIA

The bridge between `ActionStrategy` and real UI Automation. Lives in the server assembly
because it touches `AutomationElement`.

**Files:**
- Modify: `tools/DialogEditor.UiaMcp/DialogEditor.UiaMcp.csproj` (add `UseWindowsForms`)
- Create: `tools/DialogEditor.UiaMcp/ElementOperator.cs`

**Interfaces:**
- Consumes: `ActionPlan`, `ActionKind`, `ActionStrategy`, `ElementInfo` (Task 1); `UiaTree`, `Win32` (phase 1–2).
- Produces: `static string ElementOperator.Execute(UiaTree tree, ElementInfo element, ActionKind action, string? value = null)` returning a human-readable outcome, warning included.

- [x] **Step 1: Reference Windows Forms**

`ElementOperator` and `send_keys` use `System.Windows.Forms.SendKeys`, which `UseWPF` alone
does not bring in. In `tools/DialogEditor.UiaMcp/DialogEditor.UiaMcp.csproj`, add alongside
`<UseWPF>`:

```xml
    <UseWindowsForms>true</UseWindowsForms>
```

(`UseWPF` supplies the UIA client; `UseWindowsForms` supplies `SendKeys` and, with it,
`System.Drawing` for the screenshot task.)

- [x] **Step 2: Write the operator**

`tools/DialogEditor.UiaMcp/ElementOperator.cs`:

```csharp
using System.Text;
using System.Windows.Automation;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Executes an ActionStrategy plan against live UI Automation. All pattern choice lives in
/// Core; this class only carries it out and reports what happened — including, verbatim, any
/// fallback warning, because a fallback that is not reported is the failure mode the design
/// exists to prevent.
/// </summary>
internal static class ElementOperator
{
    public static string Execute(UiaTree tree, ElementInfo element, ActionKind action, string? value = null)
    {
        var plan = ActionStrategy.Plan(element, action);
        if (plan.Route == "NotOperable")
            return $"Error(NotOperable): {plan.Reason}";

        var el = tree.Element(element.Id);
        var sb = new StringBuilder();

        if (plan.NeedsScrollIntoView && !TryScrollIntoView(el, out var scrollWhy))
            return $"Error(NotOperable): could not scroll '{element.Name}' into view: {scrollWhy}";

        if (plan.NeedsScrollIntoView) sb.AppendLine("scrolled into view first.");

        switch (plan.Route)
        {
            case "Pattern":
                if (!TryPattern(el, plan.Pattern!, value, out var patternWhy))
                    return $"Error(NotOperable): {plan.Pattern} pattern failed: {patternWhy}";
                sb.AppendLine($"ok: used the {plan.Pattern} pattern.");
                break;

            case "SyntheticClick":
                if (!TryClick(el, out var clickWhy))
                    return $"Error(NotOperable): {clickWhy}";
                sb.AppendLine("ok: clicked at the element's bounding rectangle.");
                break;

            case "FocusAndType":
                el.SetFocus();
                Thread.Sleep(200);
                System.Windows.Forms.SendKeys.SendWait("^a");
                System.Windows.Forms.SendKeys.SendWait(SendKeysEscaper.EscapeLiteral(value ?? ""));
                sb.AppendLine("ok: focused and typed (select-all first).");
                break;

            case "Focus":
                el.SetFocus();
                Thread.Sleep(200);
                sb.AppendLine("ok: focused.");
                break;
        }

        if (plan.Warning is { } w) sb.AppendLine($"WARNING: {w}");
        return sb.ToString();
    }

    private static bool TryScrollIntoView(AutomationElement el, out string why)
    {
        why = "";
        try
        {
            ((ScrollItemPattern)el.GetCurrentPattern(ScrollItemPattern.Pattern)).ScrollIntoView();
            Thread.Sleep(300);
            return true;
        }
        catch (InvalidOperationException ex) { why = ex.Message; return false; }
        catch (ElementNotAvailableException ex) { why = $"the element vanished: {ex.Message}"; return false; }
    }

    private static bool TryPattern(AutomationElement el, string pattern, string? value, out string why)
    {
        why = "";
        try
        {
            switch (pattern)
            {
                case "Invoke":
                    ((InvokePattern)el.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    break;
                case "Toggle":
                    ((TogglePattern)el.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
                    break;
                case "SelectionItem":
                    ((SelectionItemPattern)el.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                    break;
                case "ExpandCollapse":
                    ((ExpandCollapsePattern)el.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                    break;
                case "Value":
                    var vp = (ValuePattern)el.GetCurrentPattern(ValuePattern.Pattern);
                    if (vp.Current.IsReadOnly) { why = "the Value pattern reports the element read-only."; return false; }
                    vp.SetValue(value ?? "");
                    break;
                default:
                    why = $"unsupported pattern '{pattern}'.";
                    return false;
            }
            Thread.Sleep(400);
            return true;
        }
        catch (InvalidOperationException ex) { why = ex.Message; return false; }
        catch (ElementNotAvailableException ex) { why = $"the element vanished: {ex.Message}"; return false; }
    }

    private static bool TryClick(AutomationElement el, out string why)
    {
        why = "";
        try
        {
            var pt = el.GetClickablePoint();
            Win32.Click((int)pt.X, (int)pt.Y);
            Thread.Sleep(500);
            return true;
        }
        catch (NoClickablePointException)
        {
            why = "the element has no clickable point (zero size, or still offscreen).";
            return false;
        }
    }
}
```

- [x] **Step 3: Build**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors. A `CS0234` on `System.Windows.Forms` means step 1 was
skipped.

- [x] **Step 4: Commit**

```bash
git add tools/DialogEditor.UiaMcp/ElementOperator.cs tools/DialogEditor.UiaMcp/DialogEditor.UiaMcp.csproj
git commit -m "feat(uiamcp): execute action plans against live UI Automation"
```

---

### Task 4: `invoke` and `focus` tools

**Files:**
- Create: `tools/DialogEditor.UiaMcp/Tools/ActionTools.cs`
- Create: `tools/DialogEditor.UiaMcp/ElementAddress.cs`

**Interfaces:**
- Consumes: `ElementOperator` (Task 3), `Resolver`, `RefTable`, `Selector`, `EditorSession` (phase 1–2).
- Produces: `static bool ElementAddress.TryResolve(EditorSession, string? reference, Selector, out ElementInfo, out string error)`; MCP tools `invoke`, `focus`.

- [x] **Step 1: Write the shared address resolution**

Both tools — and every later one — accept either a `ref` or a selector. Factor it once.

`tools/DialogEditor.UiaMcp/ElementAddress.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Turns "either a ref or a selector" into one element. Shared by every acting tool so the
/// ambiguity contract is enforced in exactly one place.
/// </summary>
internal static class ElementAddress
{
    public static bool TryResolve(
        EditorSession session, string? reference, Selector selector,
        out ElementInfo element, out string error)
    {
        element = null!;
        error = "";
        var tree = session.Tree();

        if (!string.IsNullOrWhiteSpace(reference))
        {
            if (!session.Refs.TryResolve(reference, out var elementId, out var refError))
            {
                error = $"Error(StaleRef): {refError}";
                return false;
            }

            // Re-read the element's current properties: enabled/offscreen state may have
            // moved since the ref was minted, and the plan depends on both.
            var found = new Resolver(tree).Flatten().FirstOrDefault(e => e.Id == elementId);
            if (found is null)
            {
                error = $"Error(StaleRef): ref '{reference}' no longer resolves to a live element. " +
                        "Re-run read_tree.";
                return false;
            }
            element = found;
            return true;
        }

        if (selector.IsEmpty && selector.WithinPane is null)
        {
            error = "Error(NotFound): pass either a ref, or at least one of name / controlType / automationId.";
            return false;
        }

        var result = new Resolver(tree).Resolve(selector);
        if (result.Element is null)
        {
            error = $"Error({result.ErrorKind}): {result.ErrorMessage}";
            return false;
        }
        element = result.Element;
        return true;
    }
}
```

- [x] **Step 2: Write the tools**

`tools/DialogEditor.UiaMcp/Tools/ActionTools.cs`:

```csharp
using System.ComponentModel;
using DialogEditor.UiaMcp.Core;
using ModelContextProtocol.Server;

namespace DialogEditor.UiaMcp.Tools;

[McpServerToolType]
internal sealed class ActionTools(EditorSession session)
{
    [McpServerTool, Description(
        "Activate an element: click a button, select a tab, toggle a checkbox. Prefers a real " +
        "UI Automation pattern and reports when it had to fall back to a synthetic click.")]
    public string Invoke(
        [Description("A ref_N from read_tree or find")] string? reference = null,
        [Description("Exact UIA Name")] string? name = null,
        [Description("Control type, e.g. Button")] string? controlType = null,
        [Description("Automation id")] string? automationId = null,
        [Description("Pane to scope the search to, e.g. 'Node Details'")] string? withinPane = null,
        [Description("0-based index; the only way to accept an ambiguous match")] int? nth = null)
    {
        if (!ElementAddress.TryResolve(session, reference,
                new Selector(name, controlType, automationId, withinPane, nth),
                out var element, out var error))
            return error;

        session.Foreground();
        return ElementOperator.Execute(session.Tree(), element, ActionKind.Activate);
    }

    [McpServerTool, Description("Give an element keyboard focus, scrolling it into view if needed.")]
    public string Focus(
        [Description("A ref_N from read_tree or find")] string? reference = null,
        [Description("Exact UIA Name")] string? name = null,
        [Description("Control type, e.g. Edit")] string? controlType = null,
        [Description("Automation id")] string? automationId = null,
        [Description("Pane to scope the search to")] string? withinPane = null,
        [Description("0-based index")] int? nth = null)
    {
        if (!ElementAddress.TryResolve(session, reference,
                new Selector(name, controlType, automationId, withinPane, nth),
                out var element, out var error))
            return error;

        session.Foreground();
        return ElementOperator.Execute(session.Tree(), element, ActionKind.Focus);
    }
}
```

- [x] **Step 3: Build**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors.

- [x] **Step 4: Verify against the live app**

```powershell
$repo = ((Get-Location).Path -replace '\\','/')
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Script @"
[{"name":"launch_app","arguments":{"repoRoot":"$repo"}},
 {"name":"invoke","arguments":{"name":"Language:","controlType":"ComboBox"}},
 {"name":"read_status_bar","arguments":{}}]
"@
```

**Corrected during execution:** a ComboBox exposes ExpandCollapse and no Invoke, so the
original Activate preference list (Invoke/Toggle/SelectionItem) clicked it and blamed
issue #15 for a non-defect. ExpandCollapse is now the last Activate preference and this
call reports `ok: used the ExpandCollapse pattern.` with no warning.

Expected: `invoke` reports `ok: used the ExpandCollapse pattern.` **or** a `SyntheticClick` line with a
`WARNING:` naming issue #15 — either is a pass; which one it is tells you what the ComboBox
actually exposes. An `Error(Ambiguous)` is **also** a pass if it lists both the `Text` label
and the `ComboBox`: that is the resolver refusing to guess, which is the contract.

Then confirm ambiguity is refused without a `controlType`:

```powershell
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Script @"
[{"name":"launch_app","arguments":{"repoRoot":"$repo"}},
 {"name":"invoke","arguments":{"name":"Avalonia.Controls.Viewbox"}}]
"@
```

Expected: `Error(Ambiguous)` listing 6 candidates with `nth=0..5`. **Do not "fix" this by
making it pick the first** — refusing is the whole point.

- [x] **Step 5: Commit**

```bash
git add tools/DialogEditor.UiaMcp/ElementAddress.cs tools/DialogEditor.UiaMcp/Tools/ActionTools.cs
git commit -m "feat(uiamcp): add invoke and focus tools"
```

---

### Task 5: `send_keys` and `set_value` tools

**Files:**
- Modify: `tools/DialogEditor.UiaMcp/Tools/ActionTools.cs`

**Interfaces:**
- Consumes: `SendKeysEscaper` (Task 2), `ElementOperator` (Task 3), `ElementAddress` (Task 4).
- Produces: MCP tools `send_keys`, `set_value`.

- [ ] **Step 1: Add the tools**

Append inside the `ActionTools` class in `tools/DialogEditor.UiaMcp/Tools/ActionTools.cs`:

```csharp
    [McpServerTool, Description(
        "Send a keyboard shortcut to the app window. Uses SendKeys syntax: '^w' is Ctrl+W, " +
        "'+^s' is Ctrl+Shift+S, '{ESC}' is Escape. For literal text use set_value or literalText.")]
    public string SendKeys(
        [Description("SendKeys shortcut syntax, e.g. '^w' or '{ESC}'")] string? keys = null,
        [Description("Literal text to type; metacharacters are escaped for you")] string? literalText = null)
    {
        if (string.IsNullOrEmpty(keys) && literalText is null)
            return "Error(NotFound): pass either keys (shortcut syntax) or literalText.";
        if (!string.IsNullOrEmpty(keys) && literalText is not null)
            return "Error(NotFound): pass keys OR literalText, not both — they have different escaping.";

        // SendKeys only reaches the app when its window is foreground.
        session.Foreground();

        var toSend = literalText is not null ? SendKeysEscaper.EscapeLiteral(literalText) : keys!;
        System.Windows.Forms.SendKeys.SendWait(toSend);
        Thread.Sleep(600);

        return literalText is not null
            ? $"ok: typed {literalText.Length} literal character(s) (escaped as '{toSend}')."
            : $"ok: sent '{keys}'.";
    }

    [McpServerTool, Description(
        "Set a text field's value. Prefers the UIA Value pattern and reports when it had to " +
        "fall back to focusing and typing.")]
    public string SetValue(
        [Description("The text to set")] string text,
        [Description("A ref_N from read_tree or find")] string? reference = null,
        [Description("Exact UIA Name")] string? name = null,
        [Description("Control type, e.g. Edit")] string? controlType = null,
        [Description("Automation id")] string? automationId = null,
        [Description("Pane to scope the search to")] string? withinPane = null,
        [Description("0-based index")] int? nth = null)
    {
        if (!ElementAddress.TryResolve(session, reference,
                new Selector(name, controlType, automationId, withinPane, nth),
                out var element, out var error))
            return error;

        session.Foreground();
        return ElementOperator.Execute(session.Tree(), element, ActionKind.SetValue, text);
    }
```

- [ ] **Step 2: Build**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Verify against the live app**

```powershell
$repo = ((Get-Location).Path -replace '\\','/')
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Script @"
[{"name":"launch_app","arguments":{"repoRoot":"$repo"}},
 {"name":"set_value","arguments":{"text":"neketaka","controlType":"Edit","nth":0}},
 {"name":"read_tree","arguments":{"withinPane":"Conversations"}}]
"@
```

Expected: `set_value` reports `ok: used the Value pattern.` and the subsequent `read_tree`
shows the conversation list filtered — the Conversations pane's filter box is an `Edit` with
a `Value` pattern, so this exercises the pattern route end to end.

Then a shortcut, which must be observable rather than merely "not erroring":

```powershell
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Script @"
[{"name":"launch_app","arguments":{"repoRoot":"$repo"}},
 {"name":"send_keys","arguments":{"keys":"^n"}},
 {"name":"window_title","arguments":{}}]
"@
```

Expected: `ok: sent '^n'.` Check `window_title` afterwards to see whether the shortcut had an
effect. If nothing changed, that is a finding to record, not a test to loosen — a shortcut
that silently does nothing is exactly what this tooling exists to catch.

- [ ] **Step 4: Commit**

```bash
git add tools/DialogEditor.UiaMcp/Tools/ActionTools.cs
git commit -m "feat(uiamcp): add send_keys and set_value tools"
```

---

### Task 6: `screenshot` tool

**Files:**
- Create: `tools/DialogEditor.UiaMcp/Tools/ScreenshotTool.cs`
- Modify: `tools/DialogEditor.UiaMcp/EditorSession.cs` (expose the window handle)

**Interfaces:**
- Consumes: `EditorSession`, `Win32` (phase 1–2).
- Produces: `IntPtr EditorSession.WindowHandle`; `Win32.GetWindowRect`; MCP tool `screenshot` returning `IEnumerable<ContentBlock>`.

- [ ] **Step 1: Add the window rect helper**

Append to `tools/DialogEditor.UiaMcp/Win32.cs`, inside the `Win32` class:

```csharp
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hWnd, out RECT r);

    internal struct RECT { public int Left, Top, Right, Bottom; }
```

- [ ] **Step 2: Expose the window handle**

Add to the `EditorSession` class body in `tools/DialogEditor.UiaMcp/EditorSession.cs`:

```csharp
    public IntPtr WindowHandle => _process?.MainWindowHandle
        ?? throw new InvalidOperationException("NoSession: call launch_app first.");
```

- [ ] **Step 3: Write the tool**

`tools/DialogEditor.UiaMcp/Tools/ScreenshotTool.cs`:

```csharp
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DialogEditor.UiaMcp.Tools;

[McpServerToolType]
internal sealed class ScreenshotTool(EditorSession session)
{
    [McpServerTool, Description(
        "Capture the app window as a PNG and return it inline, so the result can actually be " +
        "looked at rather than trusted.")]
    public IEnumerable<ContentBlock> Screenshot()
    {
        session.Foreground();

        if (!Win32.GetWindowRect(session.WindowHandle, out var r))
            return [new TextContentBlock { Text = "Error(NotOperable): GetWindowRect failed." }];

        var width = r.Right - r.Left;
        var height = r.Bottom - r.Top;
        if (width <= 0 || height <= 0)
            return [new TextContentBlock
                { Text = $"Error(NotOperable): window has no area ({width}x{height}); it may be minimized." }];

        using var bmp = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size);

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);

        return
        [
            new TextContentBlock { Text = $"{width}x{height} capture of '{session.Status()}'." },
            ImageContentBlock.FromBytes(ms.ToArray(), "image/png"),
        ];
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors. If `ImageContentBlock` or `ContentBlock` do not resolve,
check the namespace against the installed `ModelContextProtocol` 2.2.0 package rather than
guessing — the SDK moved these types between versions.

- [ ] **Step 5: Verify against the live app**

```powershell
$repo = ((Get-Location).Path -replace '\\','/')
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Script @"
[{"name":"launch_app","arguments":{"repoRoot":"$repo"}},
 {"name":"screenshot","arguments":{}}]
"@ 2>&1 | Select-Object -First 6
```

Expected: a text line giving the pixel dimensions, then a large base64 blob. `drive.ps1`
prints only `type == 'text'` blocks, so seeing the dimensions line plus no error is the pass
condition here; the image itself is verified when an MCP client renders it.

**A blank or black capture is a real failure**, usually meaning the window was not foreground
or had not painted. Do not accept dimensions alone as proof the pixels are right — view it
through a client before ticking this.

- [ ] **Step 6: Commit**

```bash
git add tools/DialogEditor.UiaMcp/Tools/ScreenshotTool.cs tools/DialogEditor.UiaMcp/Win32.cs tools/DialogEditor.UiaMcp/EditorSession.cs
git commit -m "feat(uiamcp): add screenshot tool returning an inline PNG"
```

---

### Task 7: `invoke_menu` tool

The highest-value action: it is how a verification run exercises a command.

**Files:**
- Modify: `tools/DialogEditor.UiaMcp/Tools/InspectionTools.cs` (extract menu navigation)
- Create: `tools/DialogEditor.UiaMcp/MenuNavigator.cs`
- Modify: `tools/DialogEditor.UiaMcp/Tools/ActionTools.cs`

**Interfaces:**
- Consumes: `ElementOperator` (Task 3), `Resolver`, `UiaTree`, `EditorSession`.
- Produces: `static bool MenuNavigator.TryWalk(EditorSession, string[] path, out ElementInfo leaf, out string log, out string error)`; MCP tool `invoke_menu`.

- [ ] **Step 1: Extract menu navigation from `menu` into `MenuNavigator`**

`tools/DialogEditor.UiaMcp/MenuNavigator.cs`:

```csharp
using System.Text;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Walks the app's menu tree, opening each ancestor along the way.
///
/// Two audit findings are baked in. The app's Menu is ANONYMOUS, so ClassName is the only
/// handle — and scoping to it is what stops the OS "System" MenuItem being treated as a peer
/// of File/Edit/View/Test/Help (clicking that one wedged the original audit probe). And the
/// app's top-level MenuItems expose neither Invoke nor ExpandCollapse, so opening them needs
/// synthetic input; ActionStrategy reports that as the defect it is (issue #15).
/// </summary>
internal static class MenuNavigator
{
    public static bool TryWalk(
        EditorSession session, string[] path,
        out ElementInfo leaf, out string log, out string error)
    {
        // All three out params must be assigned before ANY return, including the failure
        // paths below — otherwise this does not compile (CS0177).
        leaf = null!;
        error = "";
        log = "";
        var sb = new StringBuilder();
        var tree = session.Tree();

        var appMenu = new Resolver(tree).Flatten()
            .FirstOrDefault(e => e.ClassName == "Menu" && e.ControlType == "Menu");
        if (appMenu is null)
        {
            error = "Error(NotFound): the app's menu (ClassName='Menu') was not found.";
            return false;
        }

        if (path.Length == 0)
        {
            error = "Error(NotFound): pass a menu path, e.g. [\"File\", \"Save Project\"].";
            return false;
        }

        var current = appMenu;
        for (var i = 0; i < path.Length; i++)
        {
            var segment = path[i];
            var next = tree.ChildrenOf(current.Id)
                .FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.Ordinal));

            if (next is null)
            {
                var available = string.Join(", ",
                    tree.ChildrenOf(current.Id).Where(c => c.Name.Length > 0).Select(c => $"'{c.Name}'"));
                error = $"Error(NotFound): no menu item '{segment}' under " +
                        $"'{Label(current)}'. Available: {available}. Menu labels use the " +
                        "ellipsis character '…', not three dots, and are localised — no menu " +
                        "item carries an AutomationId (issue #15 finding 4).";
                return false;
            }

            // Open every ancestor, but never the leaf: opening the leaf IS invoking it, and
            // that decision belongs to the caller.
            if (i < path.Length - 1)
            {
                session.Foreground();
                var opened = ElementOperator.Execute(tree, next, ActionKind.Expand);
                if (opened.StartsWith("Error(", StringComparison.Ordinal))
                {
                    error = opened;
                    return false;
                }
                sb.Append($"opened '{segment}': {opened}");
            }

            current = next;
        }

        leaf = current;
        log = sb.ToString();
        return true;
    }

    private static string Label(ElementInfo e) => e.Name.Length > 0 ? e.Name : "the menu bar";
}
```

- [ ] **Step 2: Point `menu` at the navigator**

In `tools/DialogEditor.UiaMcp/Tools/InspectionTools.cs`, replace the whole body of the `Menu`
method with a version that delegates, so navigation exists in one place:

```csharp
    [McpServerTool, Description(
        "List the app's menu items with enabled state. Omit path for the top-level bar. " +
        "Always scoped to the app's own menu, never the OS window menu.")]
    public string Menu([Description("Menu path, e.g. ['File']")] string[]? path = null)
    {
        var tree = session.Tree();

        ElementInfo container;
        var prefix = "";

        if (path is null || path.Length == 0)
        {
            var appMenu = new Resolver(tree).Flatten()
                .FirstOrDefault(e => e.ClassName == "Menu" && e.ControlType == "Menu");
            if (appMenu is null) return "Error(NotFound): the app's menu (ClassName='Menu') was not found.";
            container = appMenu;
        }
        else
        {
            // Walk to the PARENT, then open the last segment, so its children are listed.
            if (!MenuNavigator.TryWalk(session, path, out var leaf, out var log, out var error))
                return error;
            prefix = log;

            var opened = ElementOperator.Execute(tree, leaf, ActionKind.Expand);
            if (opened.StartsWith("Error(", StringComparison.Ordinal)) return opened;
            prefix += opened;
            container = leaf;
        }

        var items = tree.ChildrenOf(container.Id).Where(c => c.ControlType == "MenuItem").ToList();
        if (items.Count == 0)
            return prefix + $"'{(container.Name.Length > 0 ? container.Name : "the menu bar")}' has no child menu items.";

        var sb = new StringBuilder(prefix);
        foreach (var item in items)
            sb.AppendLine($"{item.Name} | enabled={item.IsEnabled} | id='{item.AutomationId}'");
        return sb.ToString();
    }
```

Add `using DialogEditor.UiaMcp.Core;` to the file's usings if it is not already there, and
delete the now-unused private `TryOpen` helper and its `System.Windows.Automation` using.

- [ ] **Step 3: Add `invoke_menu`**

Append inside the `ActionTools` class in `tools/DialogEditor.UiaMcp/Tools/ActionTools.cs`:

```csharp
    [McpServerTool, Description(
        "Invoke a menu command by path, e.g. ['File','Save Project']. Opens each ancestor menu, " +
        "then activates the leaf. Refuses when the leaf is disabled.")]
    public string InvokeMenu(
        [Description("Menu path from the top-level bar to the command")] string[] path)
    {
        if (!MenuNavigator.TryWalk(session, path, out var leaf, out var log, out var error))
            return error;

        // A disabled command is a legitimate state, not a failure to route around — clicking
        // it anyway would report success while nothing happened.
        if (!leaf.IsEnabled)
            return log + $"Error(NotOperable): menu item '{leaf.Name}' is disabled " +
                   "(its command's CanExecute is false in the current app state).";

        session.Foreground();
        return log + ElementOperator.Execute(session.Tree(), leaf, ActionKind.Activate);
    }
```

- [ ] **Step 4: Build**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Verify against the live app**

First that `menu` still behaves as it did before the refactor:

```powershell
$repo = ((Get-Location).Path -replace '\\','/')
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Script @"
[{"name":"launch_app","arguments":{"repoRoot":"$repo"}},
 {"name":"menu","arguments":{}},
 {"name":"menu","arguments":{"path":["File"]}}]
"@
```

Expected: the bare call lists exactly `File, Edit, View, Test, Help` and **not** `System`;
the path call lists File's 15 items with `Save Project` disabled while projectless.

Then the disabled-command refusal, and a real command:

```powershell
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Script @"
[{"name":"launch_app","arguments":{"repoRoot":"$repo"}},
 {"name":"invoke_menu","arguments":{"path":["File","Save Project"]}},
 {"name":"invoke_menu","arguments":{"path":["Help","About…"]}},
 {"name":"window_title","arguments":{}}]
"@
```

Expected: `Save Project` returns `Error(NotOperable): … is disabled` (correct — no project is
open). `About…` opens the About window; note the ellipsis is `…`, not three dots. Confirm the
About window appeared, then check that `kill_app` still tears everything down and restores
settings.

- [ ] **Step 6: Commit**

```bash
git add tools/DialogEditor.UiaMcp/MenuNavigator.cs tools/DialogEditor.UiaMcp/Tools/ActionTools.cs tools/DialogEditor.UiaMcp/Tools/InspectionTools.cs
git commit -m "feat(uiamcp): add invoke_menu and extract shared menu navigation"
```

---

### Task 8: `DialogEditor.UiaMcp.Tests` — automated coverage for the live layer

Closes the deviation the foundation plan recorded: `DialogEditor.Tests` targets `net8.0` and
cannot reference the `net8.0-windows` server, so the live layer had manual verification only.

**Files:**
- Create: `tools/DialogEditor.UiaMcp.Tests/DialogEditor.UiaMcp.Tests.csproj`
- Create: `tools/DialogEditor.UiaMcp.Tests/EditorSessionFixture.cs`
- Create: `tools/DialogEditor.UiaMcp.Tests/ActionToolsGuiTests.cs`
- Modify: `DialogEditor.slnx`

**Interfaces:**
- Consumes: everything in `DialogEditor.UiaMcp`.
- Produces: a `Gui`-traited test assembly, excluded from the default run.

- [ ] **Step 1: Create the project**

`tools/DialogEditor.UiaMcp.Tests/DialogEditor.UiaMcp.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <!-- net8.0-windows because it references the server, which needs the UIA client.
         This is why these tests cannot live in DialogEditor.Tests (net8.0). -->
    <TargetFramework>net8.0-windows</TargetFramework>
    <UseWPF>true</UseWPF>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.8.0" />
    <PackageReference Include="xunit" Version="2.5.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.5.3" />
  </ItemGroup>
  <ItemGroup>
    <Using Include="Xunit" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\DialogEditor.UiaMcp\DialogEditor.UiaMcp.csproj" />
  </ItemGroup>
</Project>
```

The server's internals must be visible. Add to
`tools/DialogEditor.UiaMcp/DialogEditor.UiaMcp.csproj`:

```xml
  <ItemGroup>
    <AssemblyAttribute Include="System.Runtime.CompilerServices.InternalsVisibleToAttribute">
      <_Parameter1>DialogEditor.UiaMcp.Tests</_Parameter1>
    </AssemblyAttribute>
  </ItemGroup>
```

Add to `DialogEditor.slnx`, after the `DialogEditor.UiaMcp` line:

```xml
  <Project Path="tools/DialogEditor.UiaMcp.Tests/DialogEditor.UiaMcp.Tests.csproj" />
```

- [ ] **Step 2: Add the session fixture**

Gui exclusion is done with a class-level `[Trait("Category", "Gui")]` on the test class (see
step 3) — plain xunit, no custom attribute. A custom `GuiFactAttribute` implementing
`ITraitAttribute` needs a matching trait discoverer to work at all, and buys nothing here:

    include: dotnet test tools/DialogEditor.UiaMcp.Tests --filter "Category=Gui"
    exclude: dotnet test ... --filter "Category!=Gui"

`tools/DialogEditor.UiaMcp.Tests/EditorSessionFixture.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp.Tests;

/// <summary>
/// Launches the app once for a whole test class and tears it down after.
///
/// The settings snapshot goes to a TEST-SPECIFIC path, never the real one: these tests must
/// not touch the developer's own settings.json even if they crash mid-run.
/// </summary>
public sealed class EditorSessionFixture : IDisposable
{
    public EditorSession Session { get; }
    public string RepoRoot { get; }

    public EditorSessionFixture()
    {
        RepoRoot = FindRepoRoot();

        var settings = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "PillarsDialogEditor", "settings.json");
        var backup = Path.Combine(Path.GetTempPath(),
            $"PillarsDialogEditor.settings.uiamcptests.{Guid.NewGuid():N}.json");

        Session = new EditorSession(new SettingsGuard(settings, backup));
        Session.Launch(RepoRoot, "none");
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    public void Dispose()
    {
        try { Session.Kill(); } catch { /* best-effort teardown */ }
    }
}
```

- [ ] **Step 3: Write the Gui tests**

`tools/DialogEditor.UiaMcp.Tests/ActionToolsGuiTests.cs`:

```csharp
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp.Tests;

/// <summary>
/// Live-app coverage for the acting layer. These pin the behaviours that only appear against
/// a real window: pattern routes actually working, the ambiguity refusal, the synthetic-click
/// fallback being reported, and a disabled command being refused rather than clicked.
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
        // Pins audit finding 1's sibling claim and DriveApp.ps1's header. When issue #15
        // fixes this, THIS TEST SHOULD FAIL — that is the signal to delete the synthetic-click
        // fallback in ActionStrategy, per the phase 4 plan.
        var file = Resolve(new Selector(Name: "File", ControlType: "MenuItem"));

        Assert.DoesNotContain("Invoke", file.Patterns);
        Assert.DoesNotContain("ExpandCollapse", file.Patterns);

        var plan = ActionStrategy.Plan(file, ActionKind.Activate);
        Assert.Equal("SyntheticClick", plan.Route);
        Assert.Contains("#15", plan.Warning);
    }

    [Fact]
    public void ConversationRowsStillLackSelectionItem()
    {
        // Issue #15 finding 1. Also expected to fail once the app is fixed.
        var rows = new Resolver(fixture.Session.Tree()).Flatten()
            .Where(e => e.ControlType == "TreeItem").ToList();

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.DoesNotContain("SelectionItem", r.Patterns));
        Assert.All(rows, r => Assert.Contains("ScrollItem", r.Patterns));
    }

    [Fact]
    public void SettingsPathIsNotTheDevelopersRealBackupPath()
    {
        // Guards the fixture's own safety property rather than the app's behaviour.
        Assert.False(File.Exists(Path.Combine(Path.GetTempPath(),
            "PillarsDialogEditor.settings.backup.json")),
            "the Gui fixture must not use the production backup path");
    }
}
```

- [ ] **Step 4: Build and confirm the default run still excludes these**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors.

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --nologo`
Expected: PASS, unchanged from the phase 1–2 baseline plus this plan's additions. This project
is separate, so it does not appear here at all.

- [ ] **Step 5: Run the Gui tests explicitly**

Run: `dotnet test tools/DialogEditor.UiaMcp.Tests/DialogEditor.UiaMcp.Tests.csproj --filter "Category=Gui" --nologo`
Expected: PASS, 5 tests. The app window appears once and is torn down at the end.

Afterwards confirm nothing leaked:

```powershell
Get-Process -Name DialogEditor.Avalonia -ErrorAction SilentlyContinue
Get-ChildItem "$env:TEMP\PillarsDialogEditor.settings.*"
```

Expected: no process; no `settings.backup.json` and no leftover `uiamcptests` snapshot.

- [ ] **Step 6: Commit**

```bash
git add tools/DialogEditor.UiaMcp.Tests tools/DialogEditor.UiaMcp/DialogEditor.UiaMcp.csproj DialogEditor.slnx
git commit -m "test(uiamcp): add Gui-traited live-app tests for the acting layer"
```

---

### Task 9: `DriveApp.ps1` corrections

Two defects the audit found in the PowerShell harness, independent of any app change. It stays
the route for anything the server does not cover, so it should not stay wrong.

**Files:**
- Modify: `tools/ui-automation/DriveApp.ps1`

**Interfaces:**
- Consumes: nothing.
- Produces: corrected `Get-MenuItemStates`; new `Find-EditorElement`.

- [ ] **Step 1: Scope `Get-MenuItemStates` to the app's own menu**

In `tools/ui-automation/DriveApp.ps1`, replace the `Get-MenuItemStates` function with:

```powershell
function Get-MenuItemStates {
    # All MenuItem elements UNDER THE APP'S OWN MENU as "Name | enabled=…" strings.
    #
    # Scoping matters: a window-wide ControlType=MenuItem search also returns the title
    # bar's system menu ("System", AutomationId 'Item 1'), which is indistinguishable from
    # File/Edit/View/Test/Help by control type. The 2026-09-03 audit's first probe
    # enumerated that way, clicked "System", opened the OS window menu and wedged the run.
    #
    # The app's Menu has no Name, so ClassName is the only handle.
    param([Parameter(Mandatory)]$Window)

    $menuCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ClassNameProperty, 'Menu')
    $appMenu = $Window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants, $menuCond)
    if ($null -eq $appMenu) { throw "App menu not found (ClassName='Menu')." }

    $itemCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem)

    $out = @()
    foreach ($it in $appMenu.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants, $itemCond)) {
        $out += "{0} | enabled={1}" -f $it.Current.Name, $it.Current.IsEnabled
    }
    return ,$out   # comma keeps an empty result an array rather than $null
}
```

- [ ] **Step 2: Replace the `ControlType.Edit` habit with a real disambiguator**

Add after `Invoke-ElementClick`:

```powershell
function Find-EditorElement {
    # Find ONE element, erroring when the match is ambiguous instead of taking tree order.
    #
    # Invoke-ElementClick uses FindFirst on Name, which silently takes the first match in
    # tree order — and the 2026-09-03 audit confirmed eight same-surface Name collisions,
    # so that can act on the wrong control and still look successful. Filtering on
    # ControlType.Edit was the old workaround; it is coincidental, and does not help for
    # e.g. the "Language:" label colliding with its ComboBox.
    #
    # Pass -ControlType and/or -WithinPane to narrow. Panes are reliably named
    # (LeftPane / Documents / RightPane), so pane scoping is the dependable disambiguator.
    param(
        [Parameter(Mandatory)]$Window,
        [Parameter(Mandatory)][string]$Name,
        [string]$ControlType,
        [string]$WithinPane
    )

    $scope = $Window
    if ($WithinPane) {
        $paneCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $WithinPane)
        $scope = $Window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants, $paneCond)
        if ($null -eq $scope) { throw "Pane '$WithinPane' not found." }
    }

    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $matches = @($scope.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants, $cond))

    if ($ControlType) {
        $matches = @($matches | Where-Object {
            $_.Current.ControlType.ProgrammaticName -eq "ControlType.$ControlType" })
    }

    if ($matches.Count -eq 0) { throw "No element named '$Name' found." }
    if ($matches.Count -gt 1) {
        $desc = ($matches | ForEach-Object {
            "[{0}] id='{1}'" -f ($_.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''),
                                $_.Current.AutomationId }) -join '; '
        throw "'$Name' is ambiguous ($($matches.Count) matches): $desc. Narrow with -ControlType or -WithinPane."
    }
    return $matches[0]
}
```

- [ ] **Step 3: Correct the header's pattern note**

The header's claim about top-level MenuItems is **correct** and must stay. Add the measured
detail beneath it so nobody "corrects" it later. In the header comment block, after the
existing bullet about `ExpandCollapse`, add:

```powershell
#     (Verified 2026-09-03 via the MCP server's read_tree: the app's top-level MenuItems
#     expose ScrollItem ONLY. The ExpandCollapse that shows up on a MenuItem belongs to the
#     title bar's OS "System" item, not to File/Edit/View/Test/Help.)
```

- [ ] **Step 4: Verify the corrected functions against the live app**

```powershell
$repo = (Get-Location).Path
. "$repo\tools\ui-automation\DriveApp.ps1"
Initialize-DriveApp
Backup-EditorSettings
try {
    Set-EditorLastProject -ProjectPath ""
    $p = Start-DialogEditor -RepoRoot $repo
    $win = Get-EditorWindow -Process $p

    Get-MenuItemStates -Window $win          # must NOT contain "System"

    # Must throw with a candidate list rather than returning one arbitrary match:
    try { Find-EditorElement -Window $win -Name "Avalonia.Controls.Viewbox" }
    catch { "correctly refused: $_" }

    # Must succeed once narrowed:
    (Find-EditorElement -Window $win -Name "Language:" -ControlType "ComboBox").Current.ControlType.ProgrammaticName
}
finally {
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Confirm:$false; Start-Sleep 1 }
    Restore-EditorSettings
}
```

Expected: the menu list has 5 top-level entries and no `System`; the ambiguous lookup throws
listing 6 candidates; the narrowed lookup returns `ControlType.ComboBox`.

- [ ] **Step 5: Commit**

```bash
git add tools/ui-automation/DriveApp.ps1
git commit -m "fix(ui-automation): scope menu enumeration and refuse ambiguous lookups"
```

---

### Task 10: Documentation and the fallback inventory

Phase 4 deletes fallbacks as issue #15 lands. That only works if they are findable.

**Files:**
- Modify: `tools/DialogEditor.UiaMcp/README.md`
- Modify: `.claude/skills/running-the-app/SKILL.md`
- Create: `docs/uia-fallback-inventory.md`

**Interfaces:**
- Consumes: everything above.
- Produces: no code.

- [ ] **Step 1: Write the fallback inventory**

`docs/uia-fallback-inventory.md`:

```markdown
# UIA Fallback Inventory

Every place the MCP server resorts to synthetic input because the app exposes no usable UI
Automation pattern. Each entry names the issue that removes it. **Deleting a fallback is part
of the definition of done for the corresponding issue phase** — otherwise these calcify into
permanent furniture.

| # | Fallback | Why it exists | Removed when | Test that will fail first |
|---|---|---|---|---|
| 1 | Synthetic click to open a top-level menu | App `MenuItem`s expose `ScrollItem` only — no `Invoke`, no `ExpandCollapse` | #15 gives menu items an operable pattern | `ActionToolsGuiTests.TopLevelMenuItemsStillLackInvokeAndExpandCollapse` |
| 2 | Synthetic click to select a conversation row | `TreeItem`s expose `Scroll`/`ScrollItem` only — no `SelectionItem` | #15 finding 1 | `ActionToolsGuiTests.ConversationRowsStillLackSelectionItem` |
| 3 | Menu addressing by localised `Name` | 0 of 51 menu items carry an `AutomationId` | #15 finding 4 | none yet — add one with the fix |
| 4 | App menu located by `ClassName='Menu'` | The app's `Menu` element is anonymous | #15 names the menu | none yet |
| 5 | `FocusAndType` in `set_value` | Some fields expose no `Value` pattern | per-control, as found | none — data-dependent |

## How to remove one

1. Fix the app so the pattern or name exists.
2. Run the Gui test named above. **It should now fail** — that is the signal, by design.
3. Delete the fallback branch in `ActionStrategy` and the warning text with it.
4. Invert the Gui test to assert the pattern is now present.
5. Delete the row from this table.

## Why the fallbacks warn instead of staying quiet

An element reachable only by clicking its rectangle is a defect a screen-reader user hits
too. A harness that silently routed around it would let the app get less accessible while
verification runs got greener. Reporting keeps the signal load-bearing.
```

- [ ] **Step 2: Update the README's tool table and caveats**

In `tools/DialogEditor.UiaMcp/README.md`, replace the line

```
Actions — clicking, typing, screenshots — are **not** implemented yet; that is the
next phase. Use `DriveApp.ps1` for those meanwhile.
```

with:

```
Actions are implemented as of phase 3. `DriveApp.ps1` remains available and is still the
route for anything not covered here.
```

And add these rows to the tool table:

```
| `invoke` | Activate an element by ref or selector |
| `focus` | Give an element keyboard focus |
| `send_keys` | Send a shortcut, or literal text with escaping handled |
| `set_value` | Set a text field via the Value pattern |
| `invoke_menu` | Invoke a menu command by path |
| `screenshot` | Capture the window as an inline PNG |
```

Then add a section before "Manual smoke test":

```markdown
## Fallbacks

Some elements cannot be operated by pattern today, so the server uses synthetic input and
**says so in the result**, naming issue #15. `docs/uia-fallback-inventory.md` lists every
one, the test that will fail when the app is fixed, and how to remove it.

Two behaviours worth knowing:

- **`invoke_menu` refuses a disabled command** rather than clicking it. A disabled item is a
  legitimate `CanExecute` state; clicking it would report success while nothing happened.
- **Ambiguous selectors are refused, not guessed.** `invoke` with `name="Avalonia.Controls.Viewbox"`
  returns all six candidates with their `nth` indices. Narrow with `controlType`,
  `automationId` or `withinPane` — do not reach for `nth` first.

## Gui tests

`tools/DialogEditor.UiaMcp.Tests` covers the live layer. It needs a real interactive desktop
and takes over the foreground, so it is a separate project excluded from the default run:

    dotnet test tools/DialogEditor.UiaMcp.Tests --filter "Category=Gui"
```

- [ ] **Step 3: Update the skill**

In `.claude/skills/running-the-app/SKILL.md`, replace the paragraph beginning
"It does **not** yet implement actions" with:

```markdown
As of phase 3 it also acts: `invoke`, `focus`, `send_keys`, `set_value`, `invoke_menu` and
`screenshot`. It prefers real UIA patterns and reports every synthetic-input fallback,
so a result carrying a `WARNING:` about issue #15 is telling you about an app defect, not
a tooling problem. The PowerShell path below remains available and is still the route for
anything the server does not cover. See `tools/DialogEditor.UiaMcp/README.md` and
`docs/uia-fallback-inventory.md`.
```

- [ ] **Step 4: Final verification**

Run: `dotnet build "DialogEditor.slnx" -c Debug`
Expected: Build succeeded, 0 errors.

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --nologo`
Expected: PASS, no regressions against the 2349 baseline plus this plan's additions.

Run: `dotnet test tools/DialogEditor.UiaMcp.Tests/DialogEditor.UiaMcp.Tests.csproj --filter "Category=Gui" --nologo`
Expected: PASS, 5 tests.

```powershell
Get-Process -Name DialogEditor.Avalonia,DialogEditor.UiaMcp -ErrorAction SilentlyContinue
Get-ChildItem "$env:TEMP\PillarsDialogEditor.settings.*"
```

Expected: no processes, no leftover snapshots.

- [ ] **Step 5: Commit**

```bash
git add docs/uia-fallback-inventory.md tools/DialogEditor.UiaMcp/README.md .claude/skills/running-the-app/SKILL.md
git commit -m "docs(uiamcp): document actions and inventory every synthetic-input fallback"
```

---

## Out of scope — Phase 4

- **Deleting fallbacks** as each issue #15 phase lands, per `docs/uia-fallback-inventory.md`.
- **Promoting `read_tree`'s addressability warnings into an enforcement test**, which the
  audit identified as the live-UIA-tree assertion #15 phases 2–4 need. Task 8 creates the
  project that can host it.
- **Per-node canvas addressability** (#15 finding 4's sibling): dialogue nodes are drawn by
  Nodify and have no per-node UIA element, so `invoke` cannot target one. Untested by this
  plan, because the scratch project has zero nodes.

The highest-leverage item to pull into #15 first remains **stable `AutomationId`s on all 51
menu items**: it would remove fallback 3, decouple `menu` and `invoke_menu` from localised
label text, and cost nothing in UX terms since automation ids are invisible to users and to
assistive tech.
