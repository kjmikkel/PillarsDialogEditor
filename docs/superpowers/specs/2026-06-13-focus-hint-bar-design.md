# Focus-hint bar for non-MainWindow dialogs — design

Addresses Gaps.md Accessibility — Assistive Technology & Keyboard, item 13:

> Focused-control hint is MainWindow-only. Item 5's status-bar hint (Part B) depends
> on MainWindow's status bar, which other windows/dialogs (SettingsWindow,
> ScriptEditorWindow, ConditionEditorWindow, FindReplaceWindow, DiffWindow, etc.)
> don't have — their AutomationProperties.HelpText (mirrored solution-wide by item
> 5's Part A) is reachable by screen readers but has no sighted-keyboard-user
> equivalent there. Worth a lightweight hint surface (e.g. a bottom hint bar) for
> dialogs once item 5's pattern proves out.

Item 5 Part B shipped for `MainWindow` (`docs/superpowers/specs/2026-06-13-helptext-and-focus-hint-design.md`).
This spec extends the same idea — surface the focused control's
`AutomationProperties.HelpText` to sighted keyboard users — to the "workhorse"
secondary windows.

## Part 1 — `FocusHintBar` shared control

New `UserControl`: `DialogEditor.Avalonia.Shared/FocusHintBar.axaml` +
`FocusHintBar.axaml.cs`, in the `DialogEditor.Avalonia.Shared` namespace (same
convention as `ThemePickerView` — a shared control referenced via `xmlns:shared`
from `DialogEditor.Avalonia`).

**Visual:** `Border` with `Background="{DynamicResource Brush.Surface.Card}"`,
`Padding="8,4"`, containing a `TextBlock` (`FontSize="11"`,
`Foreground="{DynamicResource Brush.Text.Muted}"`) — matches `MainWindow`'s
existing status-bar text styling exactly.

**Wiring:** `public void AttachTo(Window window)` adds a bubbling `GotFocus`
handler (`RoutingStrategies.Bubble`) to `window`, mirroring
`MainWindow.OnAnyGotFocus` (item 5 Part B):

```csharp
private void OnGotFocus(object? sender, GotFocusEventArgs e)
    => Text = e.Source is StyledElement el
        ? AutomationProperties.GetHelpText(el) ?? string.Empty
        : string.Empty;
```

**`Text` property:** exposes the currently-displayed hint (backed by the inner
`TextBlock.Text`) so tests can assert on it without reaching into the visual tree.

**Visibility:** always visible, even when `Text` is empty — avoids layout shift on
the first Tab press. Consistent with `MainWindow`'s status bar, which is likewise
always present (showing `StatusText` when there's no focus hint).

**Explicitly not included:**
- No `ToolTip` — this is a passive display, like the status-bar `TextBlock` it
  mirrors (CLAUDE.md's tooltip rule applies to *interactive* controls).
- No live-region/`AutomationProperties.LiveSetting` wiring — item 8's reasoning
  (focus changes are already announced by assistive tech via the normal
  focus-description mechanism; re-announcing here would be duplicate chatter)
  applies equally to these windows.
- No `IsVisible` toggle / collapse-when-empty logic.

## Part 2 — Per-window rollout

**Windows in scope (10):** `SettingsWindow`, `ScriptEditorWindow`,
`ConditionEditorWindow`, `FindReplaceWindow`, `DiffWindow`, `BatchReplaceWindow`,
`ExportConversationsWindow`, `FlowAnalyticsWindow`, `BranchesWindow`,
`GitConflictResolutionWindow` — the windows named or implied by item 13's text,
all of which have many controls (including icon-only buttons) and already carry
`AutomationProperties.HelpText` from item 5 Part A's sweep.

**Per-window change:**

1. **XAML** — add `<shared:FocusHintBar x:Name="HintBar"/>` as the new last
   element in the window's layout:
   - **Grid-based** windows (`SettingsWindow`, `ScriptEditorWindow`,
     `ConditionEditorWindow`, `FindReplaceWindow`, `BatchReplaceWindow`,
     `FlowAnalyticsWindow`, `GitConflictResolutionWindow`): extend
     `RowDefinitions` with one more `Auto` row and place the bar there.
   - **DockPanel-based** windows (`DiffWindow`, `ExportConversationsWindow`,
     `BranchesWindow`): add `<shared:FocusHintBar DockPanel.Dock="Bottom"/>`
     before the panel's fill element.
   - Add `xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"`
     to any window that doesn't already declare it (most don't yet —
     `SettingsWindow` is the only current user).

2. **Code-behind** — one line in the constructor: `HintBar.AttachTo(this);`.

## Testing

- **`FocusHintBarTests.cs`** (new, `DialogEditor.Tests/Controls` or
  `DialogEditor.Tests/Views`) — the real TDD'd behaviour, covered once at the
  component level:
  - `[AvaloniaFact]` GotFocus on an element with `HelpText` → `Text` equals that
    `HelpText`.
  - `[AvaloniaFact]` GotFocus on an element without `HelpText` → `Text` is
    cleared to `""`.

- **`FocusHintBarPresenceTests.cs`** (new, solution-wide scan, mirroring
  `AutomationHelpTextTests`/`FakeWatermarkTests`'s anchored-on-`DialogEditor.slnx`
  XML-scan pattern) — for exactly the 10 named `.axaml` files above, asserts a
  `FocusHintBar` element with `x:Name="HintBar"` is present. This is the RED
  before the XAML sweep; a future accidental removal would fail it too.

- **Representative end-to-end checks** — extend `DiffWindowTests` and
  `BranchesWindowTests` (both already construct real windows with working
  ViewModels) with a Tab-and-assert test mirroring `MainWindowFocusHintTests`:
  Tab to a control with known `HelpText`, assert `HintBar.Text` equals it. Proves
  `AttachTo` wiring works end-to-end for at least two of the ten, without needing
  to stand up full dependency graphs for the other eight just for this change.

## Out of scope

Tracked as a new Gaps.md follow-up item (16):

- The 7 small 1–3-control dialogs: `AboutWindow`, `BranchNameDialog`,
  `ChangelogWindow`, `CommitConsentDialog`, `ConflictResolutionDialog`,
  `ForceDeleteDialog`, `HistoryWindow`. These have few `HelpText` entries (mostly
  icon-only close/help buttons) and a hint bar may be more clutter than value in
  such small windows — worth a separate judgment call.
- Visibility/collapse-when-empty behaviour for `FocusHintBar`.
- Live-region announcements for these windows' hints (item 8's reasoning applies;
  not needed).
