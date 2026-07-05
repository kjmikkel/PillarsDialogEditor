# Save-Error Visibility — Design

**Date:** 2026-07-05
**Status:** Approved

## Problem

A failed save (Ctrl+S or Save Project As) reports only via the status bar
(`Status_SaveError` / `Status_SaveAsVoCopyFailed`), which is easy to miss — B-009 went
unnoticed as "Save As does nothing" precisely because the only signal was one line of
status text. A failed save is a data-loss-risk event; the user must see it.

## Decision

Route caught save exceptions into the **existing `ExceptionReportWindow`** (non-blocking;
exception type + message, scrollable stack trace, Copy button, GitHub Issues link, log
path, per-exception-type dedupe per session). Chosen over a new purpose-built modal
(more code, less diagnostic value) and over a persistent red status banner (still
missable, no detail). Status-bar messages are unchanged — the window is additive.

## Mechanism

- `App.ShowExceptionReport(Exception)` (`DialogEditor.Avalonia\App.axaml.cs`) — already
  implements dedupe + window creation for the three unhandled-exception hooks — changes
  from `private` to `public`. No behavioural change.
- `MainWindowViewModel` gains a nullable UI-layer delegate, same pattern as
  `ShowImportWarnings`:

  ```csharp
  /// Set by the UI layer to surface a caught save exception in the exception
  /// report window (status-bar text alone is too easy to miss for a failed save).
  public Action<Exception>? ReportSaveError { get; set; }
  ```

- `MainWindow.axaml.cs` wires it:
  `vm.ReportSaveError = ex => (Application.Current as App)?.ShowExceptionReport(ex);`
  Unwired (unit tests, PatchManager host), invoking it is a null-safe no-op —
  today's behaviour.

## Call sites (3)

1. `SaveProject()` catch — plain-save failures.
2. `SaveProjectAs()` catch — Save As failures (the B-009 case).
3. Save As `_vo/` copy failure: `CopyVoFolder` returns `Exception?` instead of
   `string?`; the caller uses `ex.Message` for `Status_SaveAsVoCopyFailed` and passes
   the exception to `ReportSaveError`. The project save is still not rolled back.

`OperationCanceledException` handling is unaffected (no save path catches it
specifically; picker cancel is a null return).

## Targeted cleanup in passing

`ExceptionReportWindow.axaml.cs` `IssuesLink_Click` has a bare `catch { }` (violates the
CLAUDE.md error-handling rule). Becomes `catch (Exception ex) { AppLog.Warn(...); }`.

## Testing (TDD)

VM-level, extending the existing Save As test file (plus a plain-save case):

- Save As write failure (unwritable/blocked target path) → `ReportSaveError` invoked
  with the exception; `_projectPath` still bound to the original file.
- Successful Save As → `ReportSaveError` not invoked.
- VO-copy failure → `ReportSaveError` invoked AND the project file written (partial
  failure still reported, save not rolled back).
- Plain `SaveProject` failure (e.g. `_projectPath` directory removed) →
  `ReportSaveError` invoked.
- Delegate left null → no exception (no-op safety).

The `App`/`MainWindow` wiring is untestable glue (needs a live Avalonia app instance),
consistent with how `ShowImportWarnings` wiring is treated.

## Out of scope

- Surfacing non-save errors (open/import/git failures) this way — can follow the same
  delegate pattern later if wanted.
- Any change to `ExceptionReportWindow`'s copy/framing.
