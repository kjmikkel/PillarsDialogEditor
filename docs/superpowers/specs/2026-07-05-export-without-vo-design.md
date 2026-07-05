# Export Mod Bundle for Any Saved Project — Design

**Date:** 2026-07-05
**Status:** Approved
**Gap:** `Gaps.md` › "Export Mod Bundle without VO" (resolved by descoping — see below)

## Problem

**File ▸ Export Mod Bundle…** is gated on `HasLocalVoFolder`, so a project with no
`_vo/` folder — a text-only mod — cannot produce a `.dialogpack` at all. Those modders
must share the raw `.dialogproject`, which is exactly the "loose files" outcome the
pack format exists to prevent.

## Scope decision (the descope)

The gap as originally raised proposed a with-VO / without-VO **choice** at export time.
Use-case analysis rejected the choice:

1. *Text-only mod can't export at all* — the real defect; needs no choice UI.
2. *Text-only update to a voiced mod* (spare players a re-download) — marginal: mod
   sites handle large files, re-applying the fat pack works, and publishing two
   variants confuses users about which to download.
3. *VO distributed separately* — niche; better served by separate projects if ever
   actually needed.

**Resolution: fix the gate only.** The export mirrors the project's reality — `vo/` is
included exactly when `_vo/` exists — with no option dialog. Cases 2–3 stay unserved
until someone asks. The consumer side already treats `vo/` as optional
(`DialogPackHelper.Extract` returns a nullable `VoFolderPath`; Patch Manager and CLI
apply VO-less packs unchanged), so this is purely an export-side change.

## Behaviour

- **File ▸ Export Mod Bundle…** is enabled whenever a saved project is open
  (`ProjectPath is not null`); no longer requires `_vo/`.
- The `.dialogpack` always contains `project.dialogproject` and `FORMAT.md`;
  it contains `vo/` exactly when a `_vo/` folder exists next to the project file.
- The embedded `FORMAT.md` says `vo/` is present only when the mod contains
  voice-over (today it documents `vo/` as unconditional).
- The menu tooltip (`ToolTip_Menu_ExportModBundle`) is reworded: voice-over files are
  bundled when present. Existing key, text-only change — no new strings.
- Status-bar success/error flows are unchanged.

## Components

| Unit | Change |
|---|---|
| `VoPackExporter` (`DialogEditor.Avalonia\Services`) | `ExportAsync` skips the `vo/` loop when the `_vo/` folder doesn't exist; `FormatMdContent` wording updated; the dead `CanExport` method (no callers — the menu binds `HasLocalVoFolder` directly) is deleted. |
| `MainWindowViewModel` | `HasLocalVoFolder` (whose only consumer is this menu gate) is replaced by `CanExportModBundle => ProjectPath is not null`; the four `OnPropertyChanged(nameof(HasLocalVoFolder))` sites follow the rename. |
| `MainWindow.axaml` | `IsEnabled` binding renamed to `CanExportModBundle`. |
| `Strings.axaml` | `ToolTip_Menu_ExportModBundle` reworded (no key changes). |

## Testing (strict TDD — first tests for `VoPackExporter`)

`VoPackExporterTests` (new):
1. Export with `_vo/` present → archive contains `vo/…` entries;
   `DialogPackHelper.Extract` round-trip yields a non-null `VoFolderPath`.
2. Export without `_vo/` → archive is valid, contains `project.dialogproject` and
   `FORMAT.md`, has no `vo/` entries; `Extract` yields `VoFolderPath = null`.

`MainWindowViewModel` test: `CanExportModBundle` is false with no project, true once a
saved project is open, false again after Close Project.

## Out of scope

- Any with/without-VO export choice (rejected above; revisit only on real demand).
- Consumer-side changes (Patch Manager, CLI) — none needed.
