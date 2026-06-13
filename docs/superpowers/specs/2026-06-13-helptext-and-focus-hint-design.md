# HelpText mirroring & focus hint in status bar — design

Addresses Gaps.md Accessibility — Assistive Technology & Keyboard, item 5:

> Tooltips are the sole explanation channel, and tooltips are hover-only. Keyboard
> and screen-reader users never reach them. Mirror tooltip text into
> `AutomationProperties.HelpText` (can ride along with item 1's sweep), and consider
> showing the focused control's hint in the status bar.

## Part A — Mirror `ToolTip.Tip` into `AutomationProperties.HelpText`

**Scope:** every *focusable* control that currently carries
`ToolTip.Tip="{StaticResource X}"`:
`Button`, `ToggleButton`, `CheckBox`, `RadioButton`, `TextBox`, `ComboBox`,
`AutoCompleteBox`, `NumericUpDown`, `ListBox`/`ListBoxItem`, `MenuItem`, `Slider`,
`Expander`.

**Excluded:** tooltips on non-focusable wrappers (`TextBlock`, `Border`, `DockPanel`
used to group a tooltip over static/legend content). `AutomationProperties.HelpText`
on an element that never receives keyboard focus is never announced, so mirroring
there is dead weight.

**Enforcement test:** new file `DialogEditor.Tests/Accessibility/AutomationHelpTextTests.cs`,
following `AutomationNameTests`' solution-wide XML-scan pattern (anchored on
`DialogEditor.slnx`, same `bin`/`obj`/`.worktrees` exclusions). For each in-scope
element with `ToolTip.Tip="{StaticResource X}"`, assert it also carries
`AutomationProperties.HelpText="{StaticResource X}"` — **the same resource key**,
so the two attributes can never drift apart. This test is the RED; the sweep below
is the GREEN.

**Sweep:** mechanical attribute addition — `AutomationProperties.HelpText="{StaticResource X}"`
immediately after each matching `ToolTip.Tip`, across the ~40-50 in-scope controls
in the 23 affected views. Pure copy of an existing value to a new attribute name; no
new resource strings needed.

## Part B — Focused-control hint in the status bar

**Mechanism:** `MainWindow` attaches a bubbling `GotFocus` handler at the root. On
each focus change it reads `AutomationProperties.GetHelpText(focusedElement)`
(consuming Part A's output — this also acts as a runtime check that the sweep is
correct) and sets `MainWindowViewModel.FocusHintText` to that value, or `""` if the
focused element has no `HelpText`.

**Display:** `MainWindowViewModel` gains a computed property:

```csharp
public string DisplayStatusText =>
    string.IsNullOrEmpty(FocusHintText) ? StatusText : FocusHintText;
```

Both the `StatusText` and `FocusHintText` setters raise
`PropertyChanged(nameof(DisplayStatusText))` in addition to their own
notifications. The existing status-bar `TextBlock`
(`MainWindow.axaml`, status bar row) rebinds from `{Binding StatusText}` to
`{Binding DisplayStatusText}` — a one-line XAML change. `StatusText` itself (and
its ~50 call sites for save/load/error messages) is untouched.

**Behavior:**
- Updates only on `GotFocus`, not `LostFocus`.
- Focused element has `HelpText` → hint shown, replacing whatever `StatusText`
  currently shows.
- Focused element has no `HelpText` → hint cleared, display reverts to the last
  `StatusText`.
- Focus leaving the app entirely (e.g. alt-tab) leaves the last hint in place —
  harmless, and avoids extra `LostFocus` plumbing.

## Testing

- **ViewModel unit test** (no Avalonia): `FocusHintText` / `StatusText` →
  `DisplayStatusText` precedence and fallback, including `PropertyChanged` firing
  for `DisplayStatusText` when either source changes.
- **`[AvaloniaFact]`**: Tab focus to a known button with a mirrored `HelpText`,
  assert `FocusHintText` equals the resolved resource string and
  `DisplayStatusText` reflects it; Tab to a control with no `HelpText`, assert the
  hint clears and `DisplayStatusText` reverts to `StatusText`.

## Out of scope

- Live-region announcement of the status bar (Gaps.md item 8 — separate gap item).
- Tooltips/HelpText on non-focusable elements (legend swatches, info icons).
- Status bars in other windows — only `MainWindow` has one.
