---
title: GUI runs and headless tests both write the real layout.json; Find-EditorElements must be assigned before piping
author: Mikkel
date: 2026-10-28
branch: feat/305-condition-evaluator
cost: { minutes: 38, tokens: 138515 }
id: l-88b65b6e
scope: repo
status: active
touches:
  - tools/ui-automation/DriveApp.ps1
  - DialogEditor.Avalonia/Docking/EditorDockFactory.cs
  - DialogEditor.Avalonia/Resources/Strings.axaml
  - DialogEditor.Avalonia/Views/NodeDetailView.axaml
  - DialogEditor.Tests/Docking/EditorDockFactoryTests.cs
  - docs/superpowers/plans/2026-10-28-condition-evaluator.md
  - docs/superpowers/specs/2026-10-28-condition-evaluator-design.md
  - DialogEditor.Avalonia/App.axaml
  - DialogEditor.Avalonia/Converters/EnumIndexConverter.cs
  - DialogEditor.Avalonia/Docking/GameStateTool.cs
---

## Decided
GitHub issue 305. `Backup-EditorSettings` / `Restore-EditorSettings` in `tools/ui-automation/DriveApp.ps1` now also snapshot `%LOCALAPPDATA%\PillarsDialogEditor\layout.json` (or remove one the run created). The app rewrites it on exit, so a run that opens a dock tool used to leave the tool in the developer's own layout.

## Watch out
- Headless tests that open and close `MainWindow` still save to `DockLayoutStore.DefaultPath` (MainWindow.axaml.cs saves on close, deletes on a failed load), so a `Category!=Gui` run replaces the real layout. Tracked in issue 343; back the file up yourself around test runs until it is fixed.
- `Find-EditorElements` returns its list with `return ,$list`, so `Find-EditorElements ... | Where-Object {...}` receives the whole list as ONE object and the filter yields an array: `Invoke-ElementCenterClick` then fails with "Cannot convert System.Object[] to System.Int32". Assign first (`$all = Find-EditorElements ...; $all | Where-Object ...`).
- `SelectionItemPattern.Select()` on a canvas `CanvasNodeContainer` did not change the Node Details selection in this run (Node Details kept showing the previous node); a centre click on the container did.
- `Find-EditorElements` with neither -Name nor -ControlType throws (AndCondition needs two conditions).
