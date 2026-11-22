# Design: Docking Shell Phase 2: analysis windows as dock tools

**Date:** 2026-11-09
**Issue:** #2
**Status:** Draft, waiting for the owner's sign-off. Nothing is built until it is approved
(decided on #2: "write the Phase 2 spec first … then, after sign-off, migrate all three").
**Builds on:** [Phase 1](2026-07-16-docking-shell-phase1-design.md), plus the Game State tool
(#305), which was the first tool added after layouts had already been saved to disk.

## Purpose

Three analysis views still open as separate top-level windows, outside the docking shell:

| View | Opened from | Today |
|---|---|---|
| **Flow Analytics** | Edit menu, F7 | `FlowAnalyticsWindow`, one at a time, view-owned VM, refreshes on summon and on `ConversationSaved` |
| **Reputation & Disposition Balance** | Edit menu | `RepDispositionBalanceWindow`, a new window and a new VM on every open |
| **Find in Project** | Edit menu (`FindInProjectCommand`) | `FindInProjectWindow`, a new window and a new VM on every open |

All three are non-modal, owned by the main window, and are kept open beside the canvas while
the writer works ("results stay visible while browsing"). That is what a dock tool is for. As
windows they can pile up (Find and Rep Balance open a new window each time), they are not in the
saved layout, and they can't be tabbed, pinned or auto-hidden like the other panels.

Phase 2 turns each of them into a Dock `Tool`, so they can do everything the Phase 1 tools do.

## What already exists (not to be rebuilt)

- **Guided-tour targeting is solved.** #2 asked Phase 2 to coordinate with the tour, because a
  name lookup (`FindControl`) can't see content that Dock creates. Issue #10 fixed this:
  `Docking/TourTargetResolver.cs` finds docked content by **view type**, searches floating host
  windows too, and carries the tool id so a closed tool can be revealed first. No tour step
  points at the three analysis views today (the step targets are `BrowserPanel`, `CanvasView`,
  `DetailPanel`, `HelpToggle` and `ModeBadge`), so Phase 2 adds no tour steps. If a later tour
  wants one, it adds a `DockedTargetFor` arm. The resolver is already built for that.
- **Adding a tool to layouts that predate it** has been done once already, for Game State:
  `EditorDockFactory.AddMissingTool` and `ShowTool`'s restore chain. Phase 2 generalises that
  mechanism instead of adding a second one.
- **Closing a tool hides it** (`HideToolsOnClose = true`), so a View or Edit menu command can bring it back.

## Decisions proposed (for sign-off)

Each item gives the recommended option first, with the alternatives considered.

### D1. Where the tools go: a new bottom pane, hidden until first use

Add a fourth `ToolDock`, **`BottomPane`** (`Alignment.Bottom`, about 0.28 of the height), under
the canvas document, like Visual Studio's Output and Error List area. All three tools have their
home there. In the **default layout they start hidden** (in `HiddenDockables`, with
`OriginalOwner = BottomPane`), so a new install looks exactly as it does now until the writer
opens one. Opening a tool from the menu restores it into the bottom pane, and the pane appears
then.

- *Why bottom:* the results are wide tables. Find in Project has four columns and today's window is
  720 px wide, and Rep Balance's window is 760 px. The right pane is about 0.24 of the width and
  already holds Node Details and Condition search. Tabbing the results there would squeeze both.
- *Alternative A:* tab them into `RightPane`. No new pane is needed, but the tables are cramped and
  they compete with Node Details, which the writer needs at the same time (a result is opened, and
  then edited in Node Details).
- *Alternative B:* open them **floating** by default, the closest to today's behaviour. Dock can
  float a tool, but a floating default does not survive a Reset cleanly and puts back the
  scattered-windows problem.

Needs checking at implementation: the main horizontal `ProportionalDock` becomes a vertical one,
`[horizontal(left | documents | right)] / splitter / BottomPane`, or the bottom pane sits inside
the centre column only. **Recommend the centre column only** (`documents / splitter / BottomPane` in
a vertical `ProportionalDock` between the two side panes), which matches VS and keeps the side
panes full height.

### D2. One long-lived view model per tool

A Dock tool is created once and re-attached by id when the layout loads, so each tool needs a
view model that outlives a single "open". Today two of the three are built fresh on every open
and capture the project they were opened with:

- `ProjectFindViewModel(_project, _provider, …)`, created in `MainWindowViewModel.FindInProject`
- `RepDispositionBalanceViewModel(_project ?? Empty, _provider, …, hasProject)`, created in
  `ShowRepDispositionBalanceWindow`
- `FlowAnalyticsViewModel(…)`, created in `MainWindow.FlowAnalytics_Click`. It reads everything
  through delegates, so it is already close to long-lived. Its Detach and `ConversationSaved`
  subscription are tied to the window's `Closed`.

**Recommendation:** each tool wrapper hosts a small **host VM** with an observable `Current` inner
VM, plus an empty state ("Open a game folder first" / "Open a project to search it"). The existing
commands build the inner VM exactly as they do now and **assign it to the host** instead of opening
a window. The host is re-pointed (or cleared) whenever the project or provider changes, through the
same `NotifyCanExecuteChanged` sites that already track those changes. That keeps the
three inner VMs and their tests as they are. Only where they are shown changes.

- *Alternative:* add `Rebind(project, provider)` to each inner VM. This touches three tested
  classes for no user-visible gain.

Flow Analytics' construction moves out of `MainWindow.FlowAnalytics_Click`, which is view code, into the
main VM or a factory, beside the other two. Its view-owned settings delegates (reading speed,
follow jumps) come with it, as the issue 14 comments require: the VM stays free of settings, and the
view passes the delegates in. Its `ConversationSaved` subscription moves from the
window's lifetime to the host's lifetime. **New behaviour:** while the tool is visible it also
refreshes when the open conversation changes. A window that closed got this for free on its next
open; a docked tool stays visible across a conversation switch, and would otherwise show the
old conversation's numbers.

### D3. Views: window content becomes a UserControl, and the windows are deleted

Extract each window's content into `FlowAnalyticsView`, `RepDispositionBalanceView` and
`FindInProjectView` (UserControls), with DataTemplates in `App.axaml` mapping each tool wrapper
to its view, as Phase 1 did. The code-behind moves with the content:

- Find in Project: the `FindResultDisplayRow` projection, `ResultsList_KeyDown` (Enter opens the
  selected result) and the double-tap handler. Its **Close button is removed**, because the tool
  tab's × does that.
- Rep Balance: the localised Source and Scope combo-box population.
- `HintBar.AttachTo(this)` must attach to the new UserControl, not the window. Check that
  `HintBar` supports a non-Window root; extend it if it doesn't.
- Window-level styles in `RepDispositionBalanceWindow.axaml`, "this view needs its own selected
  …" (line 30), move to `UserControl.Styles`. Re-check that they still beat the application styles.

The three `*Window` classes are deleted. Floating is still possible through Dock's own float
command, which uses `EditorHostWindow` (themed, `app.ico`).

### D4. Dock ids, the factory and saved layouts

- New permanent ids: **`FlowAnalytics`**, **`RepBalance`**, **`FindInProject`** and the pane
  **`BottomPane`**. As with the Phase 1 ids, the values are part of `layout.json` and must never
  change.
- Add the three to `WrapperIds` (their `Inner` is `[JsonIgnore]`), to the locators, and to
  `DefaultHomeId` (`→ "BottomPane"`).
- **Saved layouts from before Phase 2** have neither the tools nor the `BottomPane`.
  Generalise `AddMissingTool`: today it handles only `GameStateId`. Make it a set of tools
  added after the first release, and have it **create `BottomPane` too** when the home pane is
  missing, splitting it into the centre column. If that tree surgery turns out fragile with
  Dock 11.3, fall back to adding the missing tool **floating** and logging `AppLog.Info`. Never fall
  back to doing nothing.
- Keep to the layout learnings: save must skip `RootDock.PinnedDock`, and restore must carry
  `PinnedBounds` (issue 319).

### D5. Menus, shortcuts and reading mode

- The Edit menu items and **F7** stay where writers know them. They now call `ShowTool(id)`, then
  activate the tool and move keyboard focus into it (the Find box for Find in Project).
  Add the three to the **View** menu's tool list as well, next to Game State, so every tool
  can be found in one place.
- **Reading mode.** Flow Analytics and Find in Project are editing-only today (`IsEditingMode`
  on the menu items, and `CanFindInProject` requires editing). Rep Balance is offered in both
  modes. **Recommendation:** keep exactly those rules. When switching to reading mode, hide an
  open Flow Analytics or Find tool (Dock hides it, so switching back to editing can restore
  it). Rep Balance stays. *Open question for the owner:* Flow Analytics only reads, so is there
  still a reason to hide it from Readers? If not, offering it in both modes is simpler.

### D6. Scope boundaries

In scope: the three views listed. **Out of scope:** Validate VO, Batch Replace, Find/Replace
(in conversation), Speaker Line Browser, Compare Versions and History. These are also
non-modal windows, but each is either a task dialog that finishes and closes, or is not on #2's list.
They can follow later with the same recipe once Phase 2 has proved it.

## Cross-cutting requirements (project rules)

- **TDD.** Write failing tests first for the logic: the default layout (BottomPane plus three hidden
  tools whose `OriginalOwner` is BottomPane), `ShowTool` for each new id on a fresh layout and on a
  **pre-Phase-2 layout fixture** (the missing pane and tools get created), the round trip, the host VM's
  re-point and clear on project change, and Flow Analytics' refresh on conversation switch.
- **Localisation:** tool titles (`Dock_Tool_FlowAnalytics`, …) and the empty-state messages go in
  `Strings.axaml`. Reuse the existing window-title keys where the wording fits.
- **Tooltips / UIA:** the View menu items carry tooltips. The tool tabs are named by `Title`
  (`AccessibleDockTabs`). DriveApp scripts that find these windows by title must change to
  find the tab or the view instead. Grep `tools/ui-automation` and the `running-the-app` skill.
- **Error handling:** layout surgery failures are logged with `AppLog.Warn`, never swallowed.
- **No new windows**, so the window-icon rule is met through `EditorHostWindow` for floated tools.

## Testing

- **Unit (TDD):** the factory shape, migrating an old layout, `ShowTool`, the host VMs, and
  refresh triggers.
- **Headless view tests:** each view renders in a tool, and Enter or double-click on a Find
  result navigates.
- **GUI verification (`running-the-app`):** open each tool from its menu item and from F7, dock,
  float, pin and close, reopen from View, restart (the layout persists), start from a **saved
  pre-Phase-2 `layout.json`** (the tools appear in a new bottom pane), and switch reading and
  editing mode.

## Open questions for the owner

1. Is D1 (a bottom pane in the centre column, hidden by default) right, or should they tab
   into the right pane?
2. D5: should Flow Analytics stay editing-only?
3. Find in Project keeps its last results when hidden and shown again. Should reopening it from
   the menu clear the query, or keep it? (Recommendation: keep it, as VS does.)
