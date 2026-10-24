---
title: "UIA keyboard walks: never call Set-EditorForeground while a dialog is open"
author: Mikkel
date: 2026-10-24
branch: docs/ui-ux-pass-2026-10-24
cost: { minutes: 30, tokens: 268748 }
id: l-d55af460
scope: repo
status: active
touches:
  - docs/ui-ux/inventory.md
  - docs/ui-ux/reports/2026-10-24-whole-app.md
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/colourblind-1.00x-about.png
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/colourblind-1.00x-annotation.png
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/colourblind-1.00x-attribution.png
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/colourblind-1.00x-batch-replace.png
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/colourblind-1.00x-branches.png
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/colourblind-1.00x-changelog.png
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/colourblind-1.00x-compare-versions.png
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/matrix/colourblind-1.00x-condition-editor.png
---

## Watch out
- In a DriveApp.ps1 keyboard walk, calling Set-EditorForeground after opening a tool window or dialog (Settings, Find/Replace) brings the main window back to the front, so the Tab/Esc results then describe the main window, not the dialog. Open the dialog, then send keys without re-foregrounding.
- Avalonia windows do not close on Esc unless a button has IsCancel="True" or the window handles Key.Escape. When an Esc test says a window "stayed open", first check which window had focus; a walk whose focus stayed on the opener (e.g. the standalone Patcher's Settings/About buttons) is inconclusive, not a verdict.
- The standalone Patcher's UIA tab walk returns no focused-element names from DriveApp; treat its tab order as unverified rather than failing.
