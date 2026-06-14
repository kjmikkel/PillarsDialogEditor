# Focusable legend swatches in LegendWindow (Gaps item 12)

## Background

Gaps item 5's tooltip→`AutomationProperties.HelpText` mirroring sweep was scoped to
*focusable* controls — static info icons (legend swatches, inline "i"/help glyphs rendered
as `TextBlock`/`Border`) were skipped because they can't receive keyboard focus, so
`HelpText` would never be announced on them. Item 12 closes that gap for the 7 legend
swatches in `LegendWindow` (the "Connections" and "Node Types" sections): making each row
a keyboard-focusable tab stop with `AutomationProperties.Name`/`ToolTip.Tip`/
`AutomationProperties.HelpText` lets keyboard and screen-reader users reach the same
explanations sighted mouse users currently get only from hover.

Scope is explicitly the 7 swatches in `LegendWindow`'s "Connections" (Show Once, Always,
Never) and "Node Types" (NPC line, Player choice, Narrator, Script/automated action)
sections. `DiffWindow`, `DiffHelpWindow`, and `FlowAnalyticsWindow` have similar
swatch-plus-label shapes but are deferred (see "Out of scope" below).

## Approach

Wrap each of the 7 rows in a borderless, transparent `Button` that looks identical to
today's row but is keyboard-focusable and carries accessibility metadata. No visible text
changes.

**Why a `Button`, not `Focusable="True"` on the existing `StackPanel`:** Avalonia's generic
`ControlAutomationPeer` and default focus-visual adorner are reliable on `Button`;
`StackPanel` isn't designed to be a focus target. `FocusVisibilityTests` already pins that
the focus adorner lives in the adorner layer independent of the control template, so a
custom-styled `Button` still shows a visible focus ring.

This also matches the existing convention check —
`AutomationHelpTextTests.FocusableControlsWithTooltipsMirrorHelpText` already scans `<Button>`
elements solution-wide and will automatically enforce that `ToolTip.Tip`/
`AutomationProperties.HelpText` stay mirrored on these new rows, so no new mirroring test is
needed for that part.

## New resources

Add to `DialogEditor.Avalonia/Resources/Strings.axaml`, near the existing `Legend_*`
entries. One full-sentence string per row, used for `AutomationProperties.Name`,
`ToolTip.Tip`, and `AutomationProperties.HelpText` alike — screen readers get a real
sentence on focus, not the dash-prefixed fragments `Legend_*_Desc` were written for.

| Key | Text |
|---|---|
| `Legend_ShowOnce_Help` | "This colour marks connections that are shown to the player only once and hidden after they're selected." |
| `Legend_Always_Help` | "This colour marks connections that remain available to the player even after selection." |
| `Legend_Never_Help` | "This colour marks connections that are never shown to the player." |
| `Legend_NpcLine_Help` | "This colour marks NPC dialogue lines on the canvas." |
| `Legend_PlayerChoice_Help` | "This colour marks player choice options on the canvas, also marked with a ✦ symbol." |
| `Legend_Narrator_Help` | "This colour marks narrator text nodes on the canvas." |
| `Legend_ScriptAction_Help` | "This colour marks script and automated-action nodes on the canvas." |

## Style

A `Button.legendRow` style added to `LegendWindow.axaml`'s `<Window.Styles>`:

- `Background="Transparent"`, `BorderThickness="0"`, `Padding="0"`,
  `HorizontalContentAlignment="Stretch"` — preserves today's visual layout.
- `:pointerover`/`:pressed` selectors also forced to `Background="Transparent"` — the row
  has no click action (it exists purely to be a tab stop with an accessible name), so it
  must not suddenly look clickable.
- `MinHeight="0"` and `MinWidth="0"` — Avalonia's Fluent `Button` control theme sets a
  default `MinHeight="32"` independent of `Padding`. Each legend row today is ~17px tall
  (`FontSize="12"` plus a 4-5px bottom margin); without this override, wrapping in an
  unmodified-size `Button` would silently grow each of the 7 rows to 32px, adding ~100px of
  dead vertical space to a window tuned at `Width="360" Height="640"`. Zeroing both lets the
  `Button`'s size come entirely from its content, matching the current compact spacing.

## Markup change (per row)

Each row gains a `Button` wrapper carrying the three accessibility properties, all pointing
at the same `_Help` resource. Example — the "Show Once" row becomes:

```xml
<Button Classes="legendRow" Margin="0,0,0,5"
        AutomationProperties.Name="{StaticResource Legend_ShowOnce_Help}"
        ToolTip.Tip="{StaticResource Legend_ShowOnce_Help}"
        AutomationProperties.HelpText="{StaticResource Legend_ShowOnce_Help}">
    <StackPanel Orientation="Horizontal">
        <Border Width="34" Height="2.5" Background="{DynamicResource Brush.Connection.Default}"
                VerticalAlignment="Center" Margin="0,0,10,0"/>
        <TextBlock FontSize="12" VerticalAlignment="Center">
            <Run FontWeight="Bold" Text="{StaticResource Legend_ShowOnce}" Foreground="{DynamicResource Brush.Text.Secondary}"/>
            <Run Text="{StaticResource Legend_ShowOnce_Desc}" Foreground="{DynamicResource Brush.Text.Muted}"/>
        </TextBlock>
    </StackPanel>
</Button>
```

The `Margin` that currently lives on each row's `StackPanel` moves to the new `Button`
wrapper (so row-to-row spacing is unchanged); the inner `StackPanel`/`Border`/`TextBlock`
content is otherwise unchanged. The other 6 rows follow identically:

| Row | `_Help` key |
|---|---|
| Show Once | `Legend_ShowOnce_Help` |
| Always | `Legend_Always_Help` |
| Never | `Legend_Never_Help` |
| NPC line | `Legend_NpcLine_Help` |
| Player choice | `Legend_PlayerChoice_Help` |
| Narrator | `Legend_Narrator_Help` |
| Script/automated action | `Legend_ScriptAction_Help` |

## Tests

New `DialogEditor.Tests/Accessibility/LegendSwatchAccessibilityTests.cs`, solution-wide-scan
style (anchored on `DialogEditor.slnx`, same `IsExcluded` filter as the other a11y tests).
Asserts that `LegendWindow.axaml` contains exactly 7 `<Button Classes="legendRow">`
elements, each with `AutomationProperties.Name`, `ToolTip.Tip`, and
`AutomationProperties.HelpText` all present and equal to the same `{StaticResource ...}`
reference, and each still containing a `Border` + `TextBlock` child — so a future edit
can't silently drop the visible swatch/label while keeping the wrapper.

No FocusHintBar wiring test is needed: `LegendWindow` was not part of item 16's rollout and
this change doesn't add one (see "Out of scope").

## Out of scope

- **`FocusHintBar` for `LegendWindow`**: item 16 surveyed the 7 small dialogs and
  `LegendWindow` wasn't among them — it has no `FocusHintBar` today and this change doesn't
  add one. `AutomationProperties.HelpText` still reaches screen readers independent of that
  bar; sighted keyboard users get the same text via the (now-focusable) row's `ToolTip.Tip`
  on focus-hover, or the focus ring that highlights which row is selected.
- **`DiffWindow`/`DiffHelpWindow`/`FlowAnalyticsWindow` swatches**: surveyed but deferred.
  `DiffWindow`'s legend sits next to an already-accessible Help button with the full
  explanation; `FlowAnalyticsWindow`'s per-row icons are a different "many tab stops in a
  list" problem with different tradeoffs. Left as a future Gaps item if pursued.

## Gaps.md update

Mark item 12 ✅ implemented, scoped explicitly to `LegendWindow`'s 7 swatches, with a note
that `DiffWindow`/`DiffHelpWindow`/`FlowAnalyticsWindow` swatches were surveyed but deferred
for the reasons above.
