# Listed/Collapsible Dangling-Link Panel — Design

**Date:** 2026-06-04
**Status:** Approved (brainstorming complete; ready for implementation plan)

## Goal

Replace the diff window's count-only dangling-link warning with a collapsible
panel that lists each dangling link. Closes the deferred follow-up recorded in
`Gaps.md` under selective apply:

> "Two follow-ups left intentionally: a fuller (listed/collapsible) dangling-link
> panel, and automatic dependency-pulling."

(Only the listed/collapsible panel is in scope here; dependency-pulling stays
deferred.)

## Background

Selective apply ("bring in") lets the user cherry-pick changed nodes from one
project version into their working copy. After each apply, the result may contain
**dangling links** — a link whose target node is deleted by the same
conversation's patch. These are detected by `NodeLinkAnalyzer.Analyze(projected)`
(`DialogEditor.Patch/Diff/NodeLinkAnalyzer.cs`), which returns
`DanglingLink(string Conversation, int FromNode, int ToNode)` records (best-effort,
patch-level; full reachability deferred — the "warn, but allow" stance).

`DiffViewModel.Apply()` repopulates `ObservableCollection<DanglingLink>
DanglingLinks` on every apply. The current UI binds only `DanglingLinks.Count` to
a single amber `TextBlock` in the apply bar
(`Diff_DanglingWarning` = "{0} link(s) may not lead anywhere"). A string
`Diff_DanglingHeader` ("Heads up: some links may not lead anywhere after this.")
already exists in `Strings.axaml` but is currently unused — staged for this panel.

This feature is purely presentation; detection and the model are done.

## Decisions (from brainstorming)

- **Layout:** a collapsible panel docked above the apply bar; the count is the
  always-visible header, the per-link list is revealed on expand. Collapsed by
  default; hidden entirely when there are no dangling links.
- **Interaction:** read-only rows (no click-to-navigate). Rationale: dangling
  links describe the *applied result*, not the displayed diff canvas, so
  navigation could land nowhere or mislead — deferred.
- **Rows:** flat list, each row naming its conversation (no grouping UI).

## Design

### Component 1 — `DiffViewModel.DanglingLinkDescriptions`

Add `public ObservableCollection<string> DanglingLinkDescriptions { get; } = [];`.
In `Apply()`, alongside the existing `DanglingLinks` repopulation, clear and
refill it: one entry per dangling link via
`Loc.Format("Diff_DanglingRow", d.Conversation, d.FromNode, d.ToNode)`.

The structured `DanglingLinks` collection is retained (used by existing tests and
a future clickable-navigation follow-up); `DanglingLinkDescriptions` is the
display projection. Formatting lives in the ViewModel (not a XAML converter) so it
stays localized and unit-testable.

### Component 2 — `DiffWindow.axaml` panel

An `Expander x:Name="DanglingPanel"` docked `DockPanel.Dock="Bottom"`, placed in
markup **after** the apply-bar `Border` so it docks *above* the apply bar (and
above the status bar). Properties:
- `IsVisible` bound to the count via the existing `CountToVis` converter
  (`{Binding DanglingLinks.Count, Converter={StaticResource CountToVis}}`).
- `IsExpanded="False"` (collapsed by default).
- `Header` = the existing `Diff_DanglingWarning` count string
  (`{Binding DanglingLinks.Count, StringFormat=...}`), so the at-a-glance warning
  remains while collapsed.
- `ToolTip.Tip` = new `ToolTip_Diff_DanglingPanel`.
- Amber accent (`#e0a030`) to match the existing warning.

Expanded content:
- An intro `TextBlock` bound to `Diff_DanglingHeader` (finally used).
- `ItemsControl x:Name="DanglingList" ItemsSource="{Binding DanglingLinkDescriptions}"`
  whose item template is a `TextBlock` bound to the string (the row description).

The now-redundant count `TextBlock` is removed from the apply bar's `StackPanel`
(the apply bar keeps the `Diff_Hint` line and the Undo/Bring-in buttons).

### Component 3 — Strings

Add to `DialogEditor.Avalonia/Resources/Strings.axaml`:
- `Diff_DanglingRow` = `"{0}: node {1} → node {2} (target removed)"`
- `ToolTip_Diff_DanglingPanel` = a plain-language explanation, e.g. "Lists links
  whose destination node will not exist after bringing in these changes. They are
  allowed, but those links will lead nowhere in-game."

Reuse `Diff_DanglingWarning` (header) and `Diff_DanglingHeader` (intro). No keys
removed.

## Data flow

```
user clicks Bring in
  → DiffViewModel.Apply runs NodeApplyBuilder.Apply(...)
  → NodeLinkAnalyzer.Analyze(result) → DanglingLinks (existing)
  → DanglingLinkDescriptions refilled from DanglingLinks via Loc.Format (new)
  → DanglingPanel becomes visible (count > 0); DanglingList renders one row each
```

## Error handling

No new failure modes. `Apply()` already guards reconstruction in try/catch and
logs via `AppLog.Error`. Populating the description list is pure string formatting
over an in-memory collection.

## Testing (red/green TDD)

- **`DiffViewModelApplyTests`** (extend): after an Apply whose selection deletes a
  node that another brought-in node links to, `DanglingLinkDescriptions.Count`
  equals `DanglingLinks.Count` and is non-zero (one description per dangling
  link). With `StubStringProvider`, each description equals the key
  `"Diff_DanglingRow"` (the stub returns keys verbatim), confirming the format
  call is wired.
- **`DiffWindowTests`** (extend, headless): `DanglingPanel` is hidden when an
  apply produces no dangling links, and visible with `DanglingList.ItemCount`
  matching after an apply that produces them.

## Intentional limitations / deferred follow-ups

- **No click-to-navigate** — rows are informational only; navigation deferred
  (would use the retained `DanglingLinks` structure).
- **No grouping** by conversation — flat rows, each self-labeled.
- **Collapsed by default**, no auto-expand-on-new — the amber header is the cue.
- Detection remains patch-level best-effort (unchanged; full reachability
  deferred, consistent with the existing "warn, but allow" design).

## Out of scope (YAGNI)

- Dependency-pulling (the other selective-apply follow-up).
- Filtering/searching the list.
- Severity levels or per-row dismissal.
