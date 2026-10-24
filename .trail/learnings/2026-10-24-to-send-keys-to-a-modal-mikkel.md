---
title: "To send keys to a modal dialog in GUI runs, front the dialog's own HWND, not the process"
author: Mikkel
date: 2026-10-24
branch: fix/236-autosave-esc-keeps-recovery
cost: { minutes: 6, tokens: 26274 }
id: l-fdfeb8b2
scope: repo
status: active
touches:
  - DialogEditor.Tests/Search/EditDistanceTests.cs
  - DialogEditor.Core/Search/EditDistance.cs
  - DialogEditor.PatchCli/PatcherCommand.cs
  - DialogEditor.ViewModels/Services/TokenValidationService.cs
  - DialogEditor.Tests/ViewModels/MainWindowViewModelAutosaveTests.cs
  - DialogEditor.ViewModels/Services/AutosaveRecovery.cs
  - DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
  - DialogEditor.Avalonia/Views/AutosaveRestoreDialog.axaml.cs
  - DialogEditor.Avalonia/Views/AutosaveRestoreDialog.axaml
  - DialogEditor.Avalonia/Resources/Strings.axaml
---

## Watch out
- UI Automation lists an owned modal dialog (e.g. AutosaveRestoreDialog) as a child of the main window. `Send-EditorKeys` (DriveApp.ps1) fronts `Process.MainWindowHandle`, and `Save-ElementScreenshot` (CaptureSurfaces.ps1) walks up to the top-level element, which is the owner. Either way the main window comes to the front, not the dialog, so an Esc or Enter meant for the dialog can land behind it and the check passes or fails for the wrong reason.
- Front the dialog itself first: `[DriveAppWin32]::SetForegroundWindow([IntPtr][int64]$dlg.Current.NativeWindowHandle)`, then `[System.Windows.Forms.SendKeys]::SendWait('{ESC}')`. Clicking by name with `Invoke-ElementClick -Window $dlg` is fine, because a real click fronts the window it lands on.
- `Wait-EditorSecondaryWindow`, `Get-EditorSecondaryWindows` and `Save-ElementScreenshot` live in CaptureSurfaces.ps1, not DriveApp.ps1. Dot-source CaptureSurfaces.ps1, which loads DriveApp.ps1 itself.
- A dialog raised during startup re-open (crash recovery, git conflicts) can delay the main window past `Start-DialogEditor`'s fixed 6 s wait, so `Get-EditorWindow` throws "No UIA window". Poll `$p.MainWindowHandle` instead of relying on the sleep.
