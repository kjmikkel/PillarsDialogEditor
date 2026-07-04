# Flow Analytics Severity-Tier-as-Text Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose each Flow Analytics issue row's severity tier (error/warning) as text — screen-reader name + hover tooltip on the existing severity icon — per `docs/superpowers/specs/2026-07-04-flow-severity-text-design.md`.

**Architecture:** A computed `SeverityLabel` on `FlowIssueViewModel` (same binary rule as `FlowIssueKindToSeverityGlyphConverter`: `Unreachable` = error, everything else = warning; each side pinned by its own test), bound on the severity `Border` in `FlowAnalyticsWindow.axaml` as `ToolTip.Tip` + `AutomationProperties.Name`. No layout change.

**Tech Stack:** CommunityToolkit.Mvvm, `Loc`/`StubStringProvider` (tests assert key names), Avalonia XAML, xUnit.

## Global Constraints

- No user-visible text hard-coded in XAML or C# — everything via `Strings.axaml` keys.
- Strict red/green TDD for the new property; observe the failing test before implementing.
- `CHANGELOG.md` is frozen — do not touch it.
- No new tab stops: the text goes on the existing non-focusable icon `Border`.

---

### Task 1: `SeverityLabel` property + view bindings

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs` (`FlowIssueViewModel`, below `KindLabel` ~line 27)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (Flow Analytics block — find it with `grep -n "FlowAnalytics_Issue_Unreachable" DialogEditor.Avalonia/Resources/Strings.axaml`)
- Modify: `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml` (severity `Border`, ~line 118)
- Test: `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs` (extend)

**Interfaces:**
- Consumes: `FlowIssueViewModel(FlowIssue issue, string nodeSnippet, Action<int> navigate)`; `FlowIssue(int NodeId, FlowIssueKind Kind)`; `Loc.Get` (test constructor already runs `Loc.Configure(new StubStringProvider())`, which echoes keys).
- Produces: `string FlowIssueViewModel.SeverityLabel`; string keys `FlowAnalytics_Severity_Error`, `FlowAnalytics_Severity_Warning`.

- [x] **Step 1: Write the failing test**

Append inside the class in `DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs`:

```csharp
    // ── Severity tier as text (2026-07-04) ───────────────────────────────

    [Theory]
    [InlineData(FlowIssueKind.Unreachable,              "FlowAnalytics_Severity_Error")]
    [InlineData(FlowIssueKind.PlayerDeadEnd,            "FlowAnalytics_Severity_Warning")]
    [InlineData(FlowIssueKind.EmptyText,                "FlowAnalytics_Severity_Warning")]
    [InlineData(FlowIssueKind.NoIncomingLinks,          "FlowAnalytics_Severity_Warning")]
    [InlineData(FlowIssueKind.BarkTextTooLong,          "FlowAnalytics_Severity_Warning")]
    [InlineData(FlowIssueKind.BarkHasPlayerChoiceChild, "FlowAnalytics_Severity_Warning")]
    public void SeverityLabel_MapsKindToTierKey(FlowIssueKind kind, string expectedKey)
    {
        var vm = new FlowIssueViewModel(new FlowIssue(1, kind), "snippet", _ => { });
        Assert.Equal(expectedKey, vm.SeverityLabel);   // StubStringProvider echoes keys
    }
```

If `FlowIssueKind`/`FlowIssue` are not already in scope, add `using DialogEditor.Core.Analytics;` to the file's usings.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test --nologo --filter "FullyQualifiedName~FlowAnalyticsViewModelTests"`
Expected: **build failure** — `'FlowIssueViewModel' does not contain a definition for 'SeverityLabel'`.

- [x] **Step 3: Implement**

In `FlowAnalyticsViewModel.cs`, directly below the `KindLabel` property in `FlowIssueViewModel`, add:

```csharp
    /// Severity tier as text — the icon's colour/glyph carry it visually; this is
    /// the screen-reader/tooltip equivalent. Same binary rule as
    /// FlowIssueKindToSeverityGlyphConverter (each pinned by its own test so they
    /// cannot drift apart silently).
    public string SeverityLabel => Kind == FlowIssueKind.Unreachable
        ? Loc.Get("FlowAnalytics_Severity_Error")
        : Loc.Get("FlowAnalytics_Severity_Warning");
```

In `DialogEditor.Avalonia/Resources/Strings.axaml`, inside the Flow Analytics block (next to the `FlowAnalytics_Issue_*` keys), add:

```xml
    <!-- Severity tier of an issue row's icon (tooltip + screen-reader name) -->
    <sys:String x:Key="FlowAnalytics_Severity_Error">Error</sys:String>
    <sys:String x:Key="FlowAnalytics_Severity_Warning">Warning</sys:String>
```

- [x] **Step 4: Run test to verify it passes**

Run: `dotnet test --nologo --filter "FullyQualifiedName~FlowAnalyticsViewModelTests"`
Expected: PASS (6 new tests green).

- [x] **Step 5: Bind in the view**

In `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`, the severity icon `Border` (~line 118) currently reads:

```xml
                            <Border Grid.Column="0"
                                    Width="16" Height="16"
                                    CornerRadius="3"
                                    Background="{Binding Kind, Converter={StaticResource FlowIssueSeverityBrush}}"
                                    HorizontalAlignment="Center" VerticalAlignment="Center">
```

Change it to (two added lines; the inner glyph `TextBlock` stays untouched):

```xml
                            <Border Grid.Column="0"
                                    Width="16" Height="16"
                                    CornerRadius="3"
                                    Background="{Binding Kind, Converter={StaticResource FlowIssueSeverityBrush}}"
                                    ToolTip.Tip="{Binding SeverityLabel}"
                                    AutomationProperties.Name="{Binding SeverityLabel}"
                                    HorizontalAlignment="Center" VerticalAlignment="Center">
```

- [x] **Step 6: Full build + test, commit**

Run: `dotnet build && dotnet test --nologo`
Expected: build success, all tests pass.

```bash
git add DialogEditor.ViewModels/ViewModels/FlowAnalyticsViewModel.cs DialogEditor.Tests/ViewModels/FlowAnalyticsViewModelTests.cs "DialogEditor.Avalonia/Resources/Strings.axaml" "DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml"
git commit -m "feat(a11y): flow analytics severity tier as tooltip + automation name

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Gaps.md item-12 note + manual verification

**Files:**
- Modify: `Gaps.md` (accessibility item 12, the sentence deferring `DiffWindow`/`FlowAnalyticsWindow` swatches)

**Interfaces:**
- Consumes: Task 1's shipped behaviour.
- Produces: nothing downstream.

- [x] **Step 1: Update the item-12 deferral sentence**

In `Gaps.md` item 12, the current text reads:

```
    `DiffWindow`/`DiffHelpWindow`/
    `FlowAnalyticsWindow` swatches were surveyed but deferred — `DiffWindow`'s
    legend sits next to an already-accessible Help button with the full
    explanation, and `FlowAnalyticsWindow`'s per-row icons are a different "many
    tab stops in a list" problem. See
```

Replace with:

```
    `DiffWindow`/`DiffHelpWindow` swatches remain a documented won't-do —
    `DiffWindow`'s legend sits next to an already-accessible Help button with the
    full explanation. `FlowAnalyticsWindow`'s per-row icons: ✅ resolved
    (2026-07-04) — each issue row's severity tier (error/warning) is now textual
    via `SeverityLabel` (tooltip + `AutomationProperties.Name` on the existing
    non-focusable icon, so no new tab stops; see
    docs/superpowers/specs/2026-07-04-flow-severity-text-design.md). See
```

- [x] **Step 2: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): flow analytics severity icons resolved (item 12)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [x] **Step 3: Manual verification** — `dotnet run --project DialogEditor.Avalonia`, open a conversation with issues (Edit ▸ Flow Analytics):

- [x] Hovering an issue row's severity icon shows "Error" (unreachable) or "Warning" (others)
- [x] Rows look visually unchanged (no layout shift)

- [x] **Step 4: Report results** — any failure: fix, `dotnet build && dotnet test --nologo`, re-verify, commit as `fix(a11y): …`.

**Manual verification results (2026-07-04):** confirmed by hand — severity tooltips show
"Error"/"Warning", no layout shift. Two findings during verification, both addressed:
the plan's checklist wrongly said Test ▸ Flow Analytics (it is Edit ▸; text corrected),
and the window opened empty until Refresh was pressed — fixed in 13ce243 (analysis now
runs on every summon, also covering conversation switches between summons).
