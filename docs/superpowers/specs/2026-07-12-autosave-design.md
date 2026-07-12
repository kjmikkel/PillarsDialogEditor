# Project Autosave / Crash Recovery — Design

**Date:** 2026-07-12
**Status:** Approved
**Gap:** `Gaps.md` › "Project Autosave / Crash Recovery".
**Builds on:** the existing save path (`SaveProject` → `FoldCanvasIntoProject` →
`DialogProjectSerializer.SaveToFile`), the app-close guard
(`OnClosing` → `UnsavedChangesDialog` Save/Discard/Cancel), and the injectable-seam
test pattern (`ConfirmSaveBeforeApply`).

## Problem

The exception-report window reports crashes but does not protect unsaved work: a crash,
a kill, or a power cut loses everything since the last Ctrl+S. Pre-launch is the right
time — data loss is a first impression a public release doesn't recover from.

## Design

### 1. The sidecar

`<project>.dialogproject.autosave` next to the project file, written with the existing
`DialogProjectSerializer` — a recovery file **is** a project file, same schema, no new
format. The real project file is never touched by autosave.

### 2. Writing — `MainWindowViewModel.AutosaveTick()`

Driven by an Avalonia `DispatcherTimer` wired in `MainWindow.axaml.cs`
(**60-second interval**, fixed; a configurable interval is deferred). The timer runs on
the UI thread, so the tick can reuse `FoldCanvasIntoProject()` directly — the fold is
idempotent and is exactly what `SaveProject` does first.

Tick behaviour:
- No project open, no project path, or `IsModified == false` → no-op. A clean session
  never writes a sidecar.
- Otherwise: fold the canvas into the project, serialize to the sidecar path.
- Autosave does **not** clear `IsModified` (it is not a save), does **not** touch the
  status bar (silent; `AppLog.Info` on success), and any IO/serialization failure is
  caught and logged via `AppLog.Warn` — autosave must never interrupt writing.

### 3. Deleting — respecting deliberate decisions

The sidecar is deleted when its contents stop representing "work the user could lose":

- **Successful `SaveProject` / `SaveProjectAs`** — the changes are now in the real file.
  (Save As also deletes the *old* path's sidecar; the new path gets one on the next tick.)
- **Deliberate discard** — the app-close guard's Discard choice and the Close Project
  command's discard path. If the user consciously threw edits away, the next launch must
  not resurrect them.

The sidecar survives only what it exists for: crashes, kills, power loss.

### 4. Restoring

One shared hook in the project-load path (covers both the startup auto-reopen and a
manual open):

- `AutosaveRecovery.Check(projectPath)` (pure, `DialogEditor.ViewModels/Services`):
  returns recovery info when `<path>.autosave` exists **and** its `LastWriteTime` is
  newer than the project file's; a *stale* sidecar (older — the project was saved through
  some other route) is reported as stale so the caller silently deletes it.
- When recovery is offered (injectable seam on `MainWindowViewModel`, mirroring
  `ConfirmSaveBeforeApply`; the View wires a small dialog):
  - **Restore unsaved changes** — the sidecar's *content* is loaded as the project while
    `_projectPath` stays the real path, and `IsModified` is set **true**: the restored
    state exists only in memory until the user explicitly saves. The sidecar is kept
    until that save (a second crash before saving must not lose the recovery).
  - **Discard** — the sidecar is deleted and the real file loads normally.
- The dialog names the sidecar's timestamp so the user knows what they are restoring.

### 5. Cross-cutting rules (CLAUDE.md)

- **Localisation** — dialog title/message/buttons and tooltips are resources; the
  message formats the recovery timestamp via `Loc.Format`.
- **Tooltips / UIA** — dialog buttons carry ToolTip + mirrored HelpText and are
  name-bearing (structural suites cover them).
- **Error handling** — every catch logs via `AppLog.Warn`/`Error`; a corrupt sidecar is
  treated as absent (logged, deleted on discard/restore-failure paths); no bare catch.
- **Tests run serially** — seams + temp paths; no global state.

## Testing (TDD, red first)

- **`AutosaveTick`:** dirty project → sidecar written (valid project JSON, loadable);
  clean project / no project → no file; serialization failure (locked path) → logged,
  no throw; `IsModified` stays true after a tick.
- **Deletion:** `SaveProject` removes the sidecar; the discard paths remove it; Save As
  removes the old path's sidecar.
- **`AutosaveRecovery.Check`:** newer sidecar → recovery offered; stale → stale result;
  absent → none; corrupt file handled.
- **Restore flow (seam-injected):** restore → project content equals sidecar's,
  `IsModified == true`, sidecar still present; discard → sidecar gone, real file loaded.
- **Headless dialog** construction test; enforcer suites cover strings/tooltips.
- **App verification:** edit a scratch project, kill the process, relaunch — the offer
  appears; restore shows the edits as unsaved.

## Out of scope / deferred

- Configurable autosave interval (fixed 60 s for now).
- Multiple rotating autosave generations (single sidecar).
- Autosaving to a central location for projects on read-only media.
