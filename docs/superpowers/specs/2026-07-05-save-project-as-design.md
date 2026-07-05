# Save Project As… — Design

**Date:** 2026-07-05
**Status:** Approved

## Problem

The File menu offers only **Save Project**, which always writes back to the path the
`.dialogproject` was opened from. There is no way to save the open project under a new
name or location — needed for forking a mod variant, making a safety copy before a risky
batch operation, or moving a project. (See the *No "Save As…"* entry in `Gaps.md`.)

## Decisions (settled with the user)

1. **Classic rebind semantics.** After Save As, the editor considers the *new* file open:
   subsequent Ctrl+S saves there, the window title and `AppSettings.LastProjectPath`
   update, and the original file is left untouched on disk. (Not "save a copy".)
2. **`_vo/` sidecar is copied alongside.** The user's principle: no loose files — a
   saved-as project must keep working (VO playback, validation, F5 sync, bundle export)
   without manual folder surgery. A follow-up idea — an *export without VO* variant of
   the existing `.dialogpack` bundle for lightweight distribution — is out of scope here
   and recorded as its own `Gaps.md` entry.
3. **Internal project `Name` follows the new filename**, matching how New Project derives
   `Name` from the chosen path (`DoNewProject`, `MainWindowViewModel`). Filename and
   internal name never drift; the window title reflects the fork.
4. **Shortcut: Ctrl+Shift+S** — the universal Save As convention; unused in this app.

## UI

- New `File ▸ Save Project As…` `MenuItem` in `MainWindow.axaml`, directly under
  Save Project, with `InputGesture="Ctrl+Shift+S"` (display-only in Avalonia) and a
  tooltip per the CLAUDE.md tooltip rule.
- Real key binding: a new case in `MainWindow.OnKeyDownTunnel`
  (`Key.S` with `KeyModifiers == (Control | Shift)`). The existing Ctrl+S case tests
  modifier equality (`== KeyModifiers.Control`), so it cannot false-trigger. Like
  Ctrl+S, the handler first commits a focused TextBox edit (`CanvasView.FocusEditor()`)
  before executing the command.

## Command

`SaveProjectAsCommand` (async `[RelayCommand]`) on `MainWindowViewModel`:

1. **Gate** `CanSaveProjectAs()`: `_project is not null && _projectPath is not null`.
   Deliberately **no `IsModified` requirement** — Save As of a clean project is a
   legitimate fork operation. This is why it is a separate command rather than reusing
   `CanSaveProject`. `NotifyCanExecuteChanged` wherever `SaveProjectCommand`'s is called.
2. **Picker**: `_filePicker.PickSaveFileAsync(title, suggestedName: current filename,
   ".dialogproject", …)` — same call shape as `DoNewProject`. Cancel → silent no-op.
3. **Same path chosen** → delegate to the plain save; nothing rebinds.
4. **Different path**: rename `_project` (`with { Name = Path.GetFileNameWithoutExtension(newPath) }`),
   rebind `_projectPath`, then run the existing fold-canvas-and-write logic. To avoid
   duplicating that logic, `SaveProject()`'s body is extracted into a private core
   (fold + serialize + dirty-flag reset) that both commands call; `SaveProject()` keeps
   its current observable behaviour byte-for-byte.
5. **`_vo/` copy**: when the old directory has a `_vo/` folder and the directory changed,
   recursively copy it next to the new file (plain overwrite copy). A copy failure is
   logged via `AppLog.Error` and reported in the status bar as "saved, but VO copy
   failed" — the project save itself is **not** rolled back.
6. **Satellite updates** (the same set `FinishLoad` touches): `Detail.ProjectPath`,
   `Canvas.ProjectPath`, `OnPropertyChanged(nameof(HasLocalVoFolder))`,
   `BatchImportVoAllCommand.NotifyCanExecuteChanged()`, `AppSettings.LastProjectPath`,
   `CurrentProjectName` (drives `WindowTitle`), status message.

## Strings (`Strings.axaml`)

New keys — menu header (`Menu_SaveProjectAs`), tooltip, picker dialog title
(`Dialog_SaveProjectAs`), `Status_ProjectSavedAs`, `Status_SaveAsVoCopyFailed`.
No hard-coded text anywhere (localisation rule).

## Error handling

- Serialization failure: existing `SaveProject` catch pattern — `AppLog.Error` +
  `Status_SaveError`. On failure **before** anything was written, `_projectPath` and
  `Name` must not remain rebound to a file that doesn't exist — rebind only after the
  write succeeds (write first to the new path, then swap the fields; the fold happens
  on `_project` which is path-independent).
- `_vo/` copy failure: save stands; log + distinct status message (see above).
- `OperationCanceledException`: swallowed silently per project rule (picker cancel is a
  `null` return, not an exception, but the rule applies to any await in the chain).

## Testing (TDD, red/green)

`DialogEditor.Tests` — `MainWindowViewModelTests` (or a dedicated
`MainWindowViewModelSaveAsTests`), using the existing `IFilePicker` stub:

- Save As rebinds: subsequent plain Save writes to the new path; old file unchanged.
- Internal `Name` becomes the new filename-without-extension; `WindowTitle` reflects it.
- Picker cancel → no rebind, no write, no status change.
- Same path chosen → behaves exactly like plain Save (no rename).
- Clean (unmodified) project can Save As (command executable), while plain Save stays gated.
- `_vo/` folder copied recursively when present and directory changed; absent → no copy.
- VO copy failure (e.g. destination locked) → project file still written, status reports
  the partial failure.
- `AppSettings.LastProjectPath` updated. (Note: tests touching `AppSettings` run serially
  per the project's global-state constraint.)

## Out of scope

- Export Mod Bundle *without* VO (recorded as a new `Gaps.md` entry).
- Any rename-project UI beyond the filename-driven rename.
