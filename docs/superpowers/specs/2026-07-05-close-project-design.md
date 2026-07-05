# Close Project Command — Design

**Date:** 2026-07-05
**Status:** Approved
**Gap:** `Gaps.md` › "No 'Close Project' command"

## Problem

There is no way to close the current project without opening another one or quitting.
Once `IsProjectOpen` is true it stays true for the session, and the next launch
auto-reopens `AppSettings.LastProjectPath`, so even quitting doesn't produce a
projectless editor. Users who want to return to browse mode (inspect vanilla game
conversations, no mod state) cannot.

## Decisions (with rationale)

1. **Canvas is cleared on close.** The canvas may display a conversation *with project
   patches applied*; after close that content exists nowhere, so leaving it visible
   (even read-only) is misleading, and reloading the vanilla version duplicates the
   browser-click path. Blank canvas = same state as a fresh projectless launch.
2. **`AppSettings.LastProjectPath` is cleared.** A deliberate close should stick across
   restarts; otherwise the next launch reopens the project the user just closed.
   The project remains one File ▸ Open away.
3. **Menu: after Merge Projects, shortcut Ctrl+W.** Last item of the project command
   group (New, Open, Save, Save As, Merge, **Close**). Ctrl+W is the near-universal
   close shortcut and is unused in the app today.
4. **Approach A — shared teardown helper.** `ReloadCurrentProjectFromDisk` already
   tears down project state when the project file vanishes on a git branch switch
   (`MainWindowViewModel.cs`, "not present on current branch" path). That block is
   extracted into a shared `CloseProjectCore(...)` used by both the branch-switch path
   and the new command, so "what it means to close a project" has exactly one
   definition. A `ProjectSession` extraction was rejected as YAGNI; an inline duplicate
   was rejected as drift-prone in a 2000-line file.

## Behaviour

**File ▸ Close Project** (Ctrl+W), enabled only when `IsProjectOpen`.

- Runs the existing unsaved-changes guard (`GuardDirtyThen`): with unsaved canvas
  edits the existing Save / Discard / Cancel dialog appears; **Cancel aborts the close
  with no state change**.
- On proceed:
  - Project state cleared: `SetProject(null)`, `_projectPath = null`,
    `Detail.ProjectPath`/`Canvas.ProjectPath` null, `CurrentProjectName = null`,
    `IsModified = false`, attribution cache invalidated, command gates re-evaluated
    (all via `CloseProjectCore`).
  - Canvas emptied via a new `ConversationViewModel.Clear()`;
    `CurrentConversationName = null`; `_currentFile = null`; `Detail.Clear()`.
  - `AppSettings.LastProjectPath = null` — next launch starts projectless.
  - Status bar: new `Status_ProjectClosed` message naming the closed project.
  - Window title drops the `[project]` suffix (falls out of the property changes).
  - Browser re-lists vanilla game conversations — project-only "new conversations"
    disappear (falls out of `SetProject(null)`'s existing `Browser.Load` re-scan).
- Untouched: game folder, language, browse-mode features, pending test-apply
  restores (F6 revert data is game-folder state, not project state).

**Branch-switch path keeps its semantics:** `ReloadCurrentProjectFromDisk` calls only
`CloseProjectCore` — it must **not** clear `LastProjectPath` (the file may reappear on
switching back) and must **not** clear the canvas (existing behaviour, deliberately
preserved).

## Components

| Unit | Change |
|---|---|
| `MainWindowViewModel` | New `[RelayCommand(CanExecute = nameof(IsProjectOpen))] CloseProject()` wrapping `GuardDirtyThen`; private `CloseProjectCore(string statusText)` extracted from `ReloadCurrentProjectFromDisk`; `SetProject` additionally calls `CloseProjectCommand.NotifyCanExecuteChanged()`. |
| `ConversationViewModel` | New public `Clear()` — the reset block currently inlined at the top of `Load()` (undo stack, nodes, connections, annotations, selection, search, `IsModified`, `BaseSnapshot`), plus `ConversationName` reset; `Load()` calls it. |
| `MainWindow.axaml` | Menu item after Merge Projects with `InputGesture="Ctrl+W"`, tooltip + `AutomationProperties.HelpText` (UI guidelines: tooltips mandatory). |
| `MainWindow.axaml(.cs)` | Ctrl+W case in the `OnKeyDownTunnel` switch → `CloseProjectCommand`, gated by `CanExecute` (the codebase dispatches all shortcuts there; `InputGesture` on MenuItem is display-only in Avalonia). |
| `Strings.axaml` | `Menu_CloseProject`, `ToolTip_CloseProject`, `Status_ProjectClosed` (localisation rule: no hard-coded user-visible text). |

## Testing (strict TDD)

ViewModel tests (`MainWindowViewModelCloseProjectTests`):

1. Close with open project → `IsProjectOpen` false, `ProjectPath`/`CurrentProjectName`
   null, `AppSettings.LastProjectPath` null, canvas empty, `CurrentConversationName` null.
2. Close with dirty canvas → `UnsavedChangesRequested` fires, project still open until
   proceed; `DiscardAndProceed` completes the close.
3. `CancelPendingNavigation` after close request → project unchanged.
4. Command `CanExecute` false with no project open.
5. Regression: `ReloadCurrentProjectFromDisk` on a vanished file still keeps
   `AppSettings.LastProjectPath` and the canvas content (split semantics guard).

`ConversationViewModel.Clear()` tests: clears nodes/connections/annotations/undo/
selection/`IsModified`; `Load()` after `Clear()` still works.

## Out of scope

- Close-conversation-only command (Ctrl+W stays project-level).
- Any change to `.dialogproject`/`_vo` structure (kept separate per 2026-07-05
  discussion).
