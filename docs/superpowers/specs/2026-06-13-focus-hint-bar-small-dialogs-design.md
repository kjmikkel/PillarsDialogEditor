# Focus-hint bar for small dialogs (Gaps item 16)

## Background

Gaps item 13 rolled out the shared `FocusHintBar` control (`DialogEditor.Avalonia.Shared`) —
which mirrors the focused control's `AutomationProperties.HelpText` into a passive,
status-bar-styled text region — to the 10 "workhorse" secondary windows. Item 16 split off
the remaining 7 small 1-3-control dialogs (`AboutWindow`, `BranchNameDialog`,
`ChangelogWindow`, `CommitConsentDialog`, `ConflictResolutionDialog`, `ForceDeleteDialog`,
`HistoryWindow`) as a separate judgment call: "a hint bar may be more clutter than value
relative to these windows' small size."

## Survey

For each of the 7 dialogs, every focusable control's `AutomationProperties.HelpText` was
compared against text already visible in the dialog (an adjacent label, or the control's
own caption):

| Window | Controls with HelpText | Value beyond visible text? |
|---|---|---|
| `AboutWindow` | Open Repository, Open Docs, Close | Repository/Docs: **yes** — HelpText is a full sentence ("Open the project's source-code repository in your browser.") vs. a two/three-word caption. Close: no. |
| `BranchNameDialog` | NameBox | No — HelpText == the adjacent visible label ("Branch name"). |
| `ChangelogWindow` | Close | No — HelpText == the button's own caption ("Close"). |
| `CommitConsentDialog` | MessageBox | No — HelpText == the adjacent visible label ("Message"). |
| `ConflictResolutionDialog` | Cancel, Force Apply | **Yes** — both HelpText values are full consequence explanations ("Cancel the test and leave game files unchanged." / "Apply the patch anyway, ignoring the mismatch...") vs. short captions. |
| `ForceDeleteDialog` | Cancel, Confirm | No — HelpText == each button's own caption. |
| `HistoryWindow` | Compare | **Yes** — HelpText is a full explanation of what the button does and why, vs. the short caption "Compare". |

## Decision

**Add `FocusHintBar` to the 3 windows where it surfaces genuinely new information**:
`AboutWindow`, `ConflictResolutionDialog`, `HistoryWindow`.

**Do nothing to the other 4** (`BranchNameDialog`, `CommitConsentDialog`, `ChangelogWindow`,
`ForceDeleteDialog`): every `HelpText` value in these dialogs duplicates text already on
screen, so a hint bar would only ever echo what the user can already see. This is recorded
in `Gaps.md` so the omission reads as a deliberate decision, not an oversight.

## Per-window integration

Same shared control and `AttachTo(Window)` wiring pattern established by item 13.

### ConflictResolutionDialog

Already `Width="520" SizeToContent="Height" CanResize="False"` — the window auto-sizes to
its content, so adding the bar requires no size adjustment.

- Add `xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"`.
- Add `<shared:FocusHintBar x:Name="HintBar"/>` as the last child of the root `StackPanel`
  (after the Cancel/Force button row).
- `HintBar.AttachTo(this)` in **both** constructors — the parameterless one (kept for the
  XAML loader, per its existing comment) and `ConflictResolutionDialog(PatchConflictException ex)`
  — each calls `InitializeComponent()` independently, so both need the call.

### HistoryWindow

Currently `Width="720" Height="480"`, root `Grid RowDefinitions="*,Auto"` (commit list /
button row).

- Add the `xmlns:shared` namespace.
- Change `RowDefinitions="*,Auto"` to `"*,Auto,Auto"`.
- Add `<shared:FocusHintBar Grid.Row="2" x:Name="HintBar"/>` as a new last row — mirrors the
  `FlowAnalyticsWindow` `Auto,*,Auto,Auto` → row-2-FocusHintBar pattern from item 13. No
  `ColumnSpan` needed (the Grid has no column definitions).
- `HintBar.AttachTo(this)` in both constructors (parameterless + `(HistoryViewModel vm)`),
  each of which calls `InitializeComponent()` independently.

### AboutWindow

Currently `Width="420" Height="320" CanResize="False"`, root `StackPanel`, with **every**
visible string (`AppName`, `Version`, `Description`, `License`, `Credits`, `Status`) bound
to `AboutViewModel` — i.e. the content height is data-dependent, and a fixed `Height` is
already a latent clipping risk independent of this change.

- Change `Height="320" CanResize="False"` to `SizeToContent="Height" CanResize="False"` —
  the same fix `ConflictResolutionDialog` already uses, so the window grows to fit its
  (variable) content plus the new bar instead of relying on a guessed constant.
- Add the `xmlns:shared` namespace.
- Add `<shared:FocusHintBar x:Name="HintBar"/>` as the last child of the root `StackPanel`
  (after the Close button).
- `HintBar.AttachTo(this)` goes in the parameterless constructor only — `AboutWindow(AboutViewModel viewModel) : this()`
  chains to it, so calling `AttachTo` there would double-register the handler.

## Tests

- **Presence**: extend `DialogEditor.Tests/Accessibility/FocusHintBarPresenceTests.cs` with
  a second `WindowsInScope`-style array (e.g. `WindowsInScopeItem16`) containing
  `AboutWindow.axaml`, `ConflictResolutionDialog.axaml`, `HistoryWindow.axaml`, and a second
  `[Theory]`/`[MemberData]` pair reusing the existing `WindowHasFocusHintBar` assertion
  logic. Keep it textually separate from the existing item-13 list (different `MemberData`
  method, its own doc comment referencing item 16) — the two lists document two different
  decisions and shouldn't be silently merged.
- **End-to-end wiring**: one `Tab_ToControlWithHelpText_UpdatesHintBar`-style test (matching
  the `DiffWindowTests`/`BranchesWindowTests` precedent from item 13) on
  `ConflictResolutionDialog` — its Cancel/Force buttons give the clearest "the bar's text
  changes meaningfully depending on which control is focused" demonstration. No equivalent
  wiring test is added for `AboutWindow`/`HistoryWindow`; the presence test plus the shared
  control's own unit tests (`FocusHintBarTests`) already cover the mirroring mechanism.

## Gaps.md update

Item 16 is marked implemented, recording:
- the 3-window rollout (`AboutWindow`, `ConflictResolutionDialog`, `HistoryWindow`) and the
  `SizeToContent` fix that came with `AboutWindow`;
- the explicit "no action" decision for the other 4 dialogs, with the one-line reason
  (HelpText duplicates visible text) so it reads as resolved, not skipped.
