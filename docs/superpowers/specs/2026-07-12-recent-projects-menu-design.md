# Recent Projects Menu — Design

**Date:** 2026-07-12
**Status:** Approved
**Gap:** `Gaps.md` ▸ Smaller Writer/UX Backlog ▸ "Recent projects menu"

## Problem

The editor remembers exactly one project (`AppSettings.LastProjectPath`, used for
startup auto-reopen). A writer alternating between two or more projects re-navigates
the file picker every switch. A **File ▸ Recent Projects** list is the standard fix.

## Decisions (settled in brainstorming, 2026-07-12)

1. **Shape: submenu.** File ▸ Recent Projects ▸ [entries]. The File menu keeps a
   stable length; the submenu holds the variable content. Chosen over the inline
   numbered style (Word/Notepad++) to avoid the File menu shifting as the list fills.
2. **Missing files: keep until clicked.** An entry whose file is currently absent
   (unplugged drive, renamed folder, other git branch) stays listed. Clicking it
   reports the failure and offers to remove that entry. Chosen over prune-on-open
   (silently forgets temporarily-unavailable projects) and greyed-out entries
   (uninformative, accumulate).
3. **Cap 10, MRU order, dedupe.** Most recent first; re-opening a listed path moves
   it to the front rather than duplicating. Path comparison is
   `OrdinalIgnoreCase` (Windows paths).
4. **Close Project does not touch the list.** Close clears `LastProjectPath` so the
   *auto-reopen* stops — that is intent about the next launch, not about history.
   The recent list is history and keeps the entry.
5. **Current project stays listed.** The list is plain MRU history (as in Visual
   Studio); no special-casing of the currently open path.

## Storage

`AppSettings` gains `RecentProjects` (`List<string>`, persisted in the same
`settings.json`; default empty). All mutation goes through two static helpers on
`AppSettings` so the MRU discipline lives in one place:

- `AddRecentProject(string path)` — normalises to a full path, removes any
  case-insensitive duplicate, inserts at index 0, truncates to 10, saves.
- `RemoveRecentProject(string path)` — removes (case-insensitive), saves.
- `ClearRecentProjects()` — empties, saves.

**Recording points** are exactly the three sites that already set
`AppSettings.LastProjectPath = path` to a non-null value in `MainWindowViewModel`:
successful open (`LoadProjectAsync`), `DoNewProject`, and Save As. No recording on
close, on failed opens, or anywhere else.

## ViewModel

`MainWindowViewModel`:

- `RecentProjects` — read-only view of the stored list for menu binding, refreshed
  (property-change notification) after every mutation.
- `OpenRecentProjectCommand(string path)`:
  - If the file exists → the existing `GuardDirtyThen(...)` + `LoadProjectAsync`
    funnel, so the unsaved-changes guard, git-conflict handling, and the
    autosave-restore offer all apply unchanged. (Opening the already-open project is
    harmless — same as File ▸ Open picking the current file.)
  - If missing → `AppLog.Warn`, localized status message naming the path, then a
    confirm interaction ("… could not be found. Remove it from the recent list?")
    via a delegate following the existing `RequestLanguageCode`/dialog-delegate
    pattern; **Yes** removes the entry, **No** keeps it. Headless tests drive the
    delegate directly.
- `ClearRecentProjectsCommand` — empties the list. No confirmation: the action is
  low-stakes (the list rebuilds through normal use) and the menu item's tooltip
  states the effect.

## Menu (MainWindow.axaml)

A **Recent Projects** `MenuItem` directly below **Open Project…**:

- Entries bound from `RecentProjects`; each item's header is the filename without
  extension, its `ToolTip.Tip`/`AutomationProperties.HelpText` is the full path
  (satisfies the mandatory-tooltip rule and gives screen readers and DriveApp.ps1
  a discoverable name — headers must surface as UIA Names).
- Below the entries: a separator, then **Clear Recently Opened** with a tooltip.
- When the list is empty the submenu parent is disabled, with a tooltip explaining
  that projects appear here as they are opened — discoverable, never a dead end.
- The mixed content (dynamic entries + fixed clear item) is rebuilt in thin
  code-behind on `SubmenuOpened` (mirroring the existing thin-glue pattern:
  logic in the ViewModel, wiring in the view). Chosen over `ItemsSource`
  templating because mixing bound items with a fixed separator + clear item
  fights Avalonia's menu templating for no behavioural gain. Constraints hold
  regardless: localised strings only, tooltips present, UIA names intact.

All new strings (`Menu_RecentProjects`, `Menu_ClearRecentProjects`, tooltips,
missing-file status/confirm text) live in `Strings.axaml`.

## Error handling

- Missing file on click: `AppLog.Warn` + status message + remove-offer (above).
  Never an exception path — checked with `File.Exists` before entering the funnel.
- A failed load of an *existing* file (corrupt, locked) is already handled inside
  `LoadProjectAsync`; the entry stays listed (the file exists; the problem is not
  list rot).
- Settings IO failures follow existing `AppSettings` behaviour (best-effort save).

## Testing (TDD, serial suite)

`AppSettings` tests (global state — suite already runs serially):

- Add: front insertion, case-insensitive move-to-front on duplicate, cap at 10
  (11th push evicts the oldest), persistence round-trip.
- Remove/Clear behaviour.

`MainWindowViewModel` tests:

- Open, New, and Save As each record the path; Close records nothing and does not
  remove the entry.
- `OpenRecentProjectCommand` on a missing path: fires the confirm delegate,
  removes on yes, keeps on no, warns either way; on an existing path routes into
  the normal load funnel (observable via project-open state).
- Clear command empties the persisted list.

## Out of scope

- OS jump-list / taskbar integration.
- Pinned entries.
- Showing recent projects on an empty-state canvas ("start page").
