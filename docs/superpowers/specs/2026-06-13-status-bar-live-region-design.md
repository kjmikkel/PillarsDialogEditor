# Status Bar Live Region (Gaps.md Accessibility item 8)

**Goal:** Make `MainWindow`'s status-bar feedback (save/error/operation results,
surfaced via `MainWindowViewModel.StatusText`) audible to screen-reader users, who
today receive no notification when it changes.

## Background

`Gaps.md` item 8 observes that `StatusText` is the sole feedback channel for many
operations (project opened, conversation added, import/export results, errors,
etc.) but is rendered as plain 11px `TextBlock` text with no
`AutomationProperties.LiveSetting` — screen readers never announce it.

Item 5 (just implemented) rebound the visible status-bar `TextBlock` from
`StatusText` to `MainWindowViewModel.DisplayStatusText`, which shows the
currently-focused control's `AutomationProperties.HelpText` (via
`FocusHintText`) when present, falling back to `StatusText` otherwise. If a live
region were attached to `DisplayStatusText`, every Tab/arrow-key focus move would
*also* trigger a live-region announcement of the focus hint — duplicating the
screen reader's normal "focused on X, hint: Y" announcement for that same
control. That's unwanted chatter and out of scope for item 8, which is
specifically about operation results (save/error), not focus hints.

## Design — Approach A: decouple the accessible Name from the visible Text

The status-bar `TextBlock` keeps its existing visible binding:

```xml
Text="{Binding DisplayStatusText}"
```

(unchanged — still shows focus hints while a control with `HelpText` is focused,
per item 5). Two attached properties are added to the **same** element:

```xml
AutomationProperties.Name="{Binding StatusText}"
AutomationProperties.LiveSetting="Polite"
```

- `AutomationProperties.Name` is bound to `StatusText` directly (not
  `DisplayStatusText`), so the screen-reader-visible "name" of this element
  tracks only operation results — never focus hints.
- `AutomationProperties.LiveSetting="Polite"` marks it as a non-interruptive live
  region: screen readers announce the new Name when it changes, without
  interrupting the user's current task.
- No new elements, no layout changes, no new localized strings (`StatusText` is
  already populated via existing `Loc.Get`/`Loc.Format` calls at ~20 call sites).

### Why this avoids the focus-hint double-announcement

Tabbing to a control updates `FocusHintText` → `DisplayStatusText` changes →
the *visible* `Text` updates. But `AutomationProperties.Name` (bound to
`StatusText`) does **not** change, so no live-region notification fires. Only a
genuine `StatusText` assignment (save/error/operation result) changes the Name
and triggers the "Polite" announcement.

## Open question resolved by a probe test

It's unconfirmed whether Avalonia 11.3.14 raises the UIA `NameChanged`
notification automatically when a *bound* `AutomationProperties.Name` value
updates at runtime on a plain `TextBlock` (vs. only computing it once at peer
creation), and whether a plain `TextBlock`'s automation peer is exposed in the
tree at all by default (it may default to `AutomationControlType.None` /
`IsControlElement = false`, which some UIA clients filter out of "control view").

**Plan:** write a small headless probe (`[AvaloniaFact]`) first, mirroring the
`FocusVisibilityTests` "characterization" approach (item 3): create a `TextBlock`
with a bound `AutomationProperties.Name` and `LiveSetting="Polite"`, mutate the
bound source, and inspect `AutomationPeer.GetOrCreate(...)` —
`GetAutomationControlType()`/`GetName()` before and after the mutation.

- **If the peer is exposed and the Name updates** → Approach A works as designed;
  proceed directly to the structural contract test + XAML change.
- **If the peer is `None`/not exposed, or the Name doesn't update dynamically** →
  fall back to **Approach B**: add a second, zero-size (`Width="0" Height="0"
  ClipToBounds="True"`) sibling `TextBlock` bound only to `StatusText`, with
  `AutomationProperties.Name="{Binding StatusText}"` and
  `LiveSetting="Polite"` — a dedicated live-region element separate from the
  visible display element. (If *this* also fails the probe, that's a stop-and-ask
  moment — it would mean Avalonia 11.3.14 doesn't support this pattern at all and
  a different mechanism, e.g. manually calling `RaisePropertyChangedEvent`, is
  needed; not designed here.)

## Testing

- **Probe test** (above) — informational, establishes which approach to take.
  Not a regression gate by itself.
- **Structural/contract test**, mirroring `AutomationHelpTextTests`/
  `AutomationNameTests`: pin `AutomationProperties.LiveSetting="Polite"` on the
  chosen element in `MainWindow.axaml`, so a future refactor can't silently drop
  it.
- **Binding test**: an `[AvaloniaFact]` confirming the chosen element's
  `AutomationProperties.Name` reflects `vm.StatusText` (not
  `vm.DisplayStatusText`) after `StatusText` changes while `FocusHintText` is
  set — i.e. the decoupling actually holds.

## Scope

- In scope: `MainWindow`'s status bar only (the only window item 5 gave a status
  surface to).
- Out of scope: item 13 (hint surfaces for other windows) — unrelated, separate
  gap.
- Out of scope: filtering *which* `StatusText` changes are "important enough" to
  announce — every `StatusText` assignment is operation feedback by construction
  (existing ~20 call sites), so no filtering logic is needed (YAGNI).

## Tech Stack

C#/.NET 8, Avalonia 11.3.14, CommunityToolkit.Mvvm, xUnit + Avalonia.Headless.XUnit
(`[AvaloniaFact]`).
