# Error Window for Non-Save Failures — Design

**Date:** 2026-07-05
**Status:** Approved

## Problem

The save path now surfaces caught exceptions in `ExceptionReportWindow` (see
`2026-07-05-save-error-visibility-design.md`), but every other failure family in
`MainWindowViewModel` — project open, conversation import, merge, game-data load,
backup/restore, test-apply, batch VO, sample build — still reports only via one line of
status-bar text. Tracked as the *"Non-save errors are status-bar-only"* gap in `Gaps.md`.

## Decisions

1. **Scope: `MainWindowViewModel` only.** The git tool windows (Compare/History/
   Branches/Blame) keep their existing in-window status reporting — they already show
   localized `DiffException` messages in their own UI, which is the right surface.
2. **Triage rule: log severity decides.** Every catch block in `MainWindowViewModel`
   that logs with `AppLog.Error` also invokes the report delegate; catch blocks that
   log with `AppLog.Warn` stay status/log-only. Severity already encodes
   "operation failed / data-loss-risk" vs "routine environmental noise" — no new
   classification is invented. The window's per-exception-type dedupe (one window per
   type per session) prevents floods from loops (e.g. per-file VO sync failures).
3. **Delegate rename: `ReportSaveError` → `ReportError`.** Same signature
   (`Action<Exception>?`), same null-safe no-op when unwired, same wiring point. The
   delegate is days old with no consumers outside this repo; renaming keeps the name
   honest now that it covers all error families. The five existing save tests rename
   with it.
4. **UI-thread safety in the wiring.** Some call sites run off the UI thread (the VO
   alias index rebuild inside `Task.Run`); creating a window there would crash. The
   `MainWindow` wiring therefore posts:

   ```csharp
   vm.ReportError = ex =>
       Dispatcher.UIThread.Post(() => (Application.Current as App)?.ShowExceptionReport(ex));
   ```

   mirroring what `App`'s own domain/task exception hooks already do. Every call site
   is thread-safe by construction; the ViewModel layer stays dispatcher-free.

## Affected call sites (the `AppLog.Error` catches)

Project open (`LoadProjectAsync`), conversation import, merge projects, game-data
initialisation (`LoadDirectory`), backup, restore, test-apply, project-wide batch VO
scan, sample build, apply-from-diff save, undo-apply save, VO alias index rebuild
(background), per-file VO sync copy failures. (The two apply/undo-apply catches are
save failures the previous spec missed.) `CopyVoFolder` keeps its return-the-exception
pattern with the caller invoking the delegate.

`AppLog.Warn` sites (walkthrough link launch, changelog read, unparseable git-conflict
sides, temp-folder cleanup, …) are deliberately excluded.

## Enforcement by construction

New `ErrorReportingCoverageTests` (in `DialogEditor.Tests`, mirroring the
`NoStrayHexTests` source-scan idiom): parses `MainWindowViewModel.cs`, and for every
`catch` block containing `AppLog.Error` requires that the block also contain
`ReportError?.Invoke` or `return ex;` (the `CopyVoFolder` caller-invokes pattern).
A new error site that only writes status text fails the build.

## Testing

- Rename-refactor keeps the five existing save-path delegate tests green (now against
  `ReportError`).
- Representative behavioural tests (not all 13 sites): opening a corrupt
  `.dialogproject` invokes `ReportError` with the exception; a failing conversation
  import invokes it; a successful project open does not.
- `ErrorReportingCoverageTests` covers the rest structurally.

## Out of scope

- Git tool windows and any other ViewModel.
- `AppLog.Warn` sites.
- Any change to `ExceptionReportWindow` itself.
