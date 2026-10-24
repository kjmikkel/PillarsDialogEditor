---
title: Capture-Windows.ps1 fails silently from a fresh pwsh; dialog-patcher ignores unknown flags
author: Mikkel
date: 2026-10-24
branch: docs/audience-roles
cost: { minutes: 21, tokens: 188834 }
id: l-ddc15614
scope: repo
status: active
touches:
  - tools/ui-automation/Capture-Windows.ps1
---

## Watch out
- tools/ui-automation/Capture-Windows.ps1 assigns [System.Windows.Automation.ControlType] at script scope before Initialize-DriveApp loads the UIA assemblies. From a fresh pwsh it dies with "Unable to find type" yet exits 0, so a background run looks successful with no shots. Workaround: dot-source DriveApp.ps1 and call Initialize-DriveApp in the same session before invoking the script.
- dialog-patcher ignores unknown flags: a typo like --dryrun runs a real apply. Never test CLI flag typos against a real game folder; use a scratch copy of DialogEditor.Tests/Fixtures/Canonical.
