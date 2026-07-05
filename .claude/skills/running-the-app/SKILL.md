---
name: running-the-app
description: Launch and drive the Pillars Dialog Editor GUI (Avalonia/Windows) for end-to-end verification — real window, real menus, real shortcuts, screenshots. Use this whenever a change needs to be seen working in the actual app (menu items, commands, shortcuts, window title/status behaviour), whenever the user says "run it", "run the app", "verify in the app", or asks for screenshots of the editor — unit tests passing is not the same as the app working.
---

# Running and driving the Dialog Editor

End-to-end GUI verification on the local Windows desktop: launch the built app,
drive it with UI Automation + synthetic input, screenshot the results. All the
non-obvious mechanics live in `tools/ui-automation/DriveApp.ps1` (dot-source it;
read its header comment — it documents the gotchas so you don't rediscover them).

**Prerequisites:** a Debug build (`dotnet build "DialogEditor.slnx"`), pwsh 7+,
and a real interactive desktop (screenshots/SendKeys fail on a headless session).

## The golden path

Run from pwsh with the repo root in `$repo`:

```powershell
. "$repo\tools\ui-automation\DriveApp.ps1"
Initialize-DriveApp

# 1. Protect the user's real session state — ALWAYS. The app persists
#    LastProjectPath etc. in %LOCALAPPDATA%\PillarsDialogEditor\settings.json
#    and auto-reopens that project on launch.
Backup-EditorSettings
try {
    # 2. Decide the launch state. To start WITH a project open, write a scratch
    #    project via the app's own serializer and point settings at it:
    $proj = "$env:TEMP\ScratchProject.dialogproject"
    New-ScratchProject -RepoRoot $repo -Path $proj -Name "ScratchProject"
    Set-EditorLastProject -ProjectPath $proj
    #    (To start projectless: Set-EditorLastProject -ProjectPath "")

    # 3. Launch and grab the window.
    $p   = Start-DialogEditor -RepoRoot $repo
    $win = Get-EditorWindow -Process $p
    $p.MainWindowTitle           # e.g. "Pillars Dialog Editor [ScratchProject]"

    # 4. Drive it. Menus open via real clicks (Avalonia's top-level menu items
    #    don't implement UIA ExpandCollapse); shortcuts via SendKeys.
    Invoke-ElementClick -Window $win -Name "File"
    Get-MenuItemStates  -Window $win          # names + enabled, in menu order
    Send-EditorKeys     -Process $p -Keys "{ESC}"
    Send-EditorKeys     -Process $p -Keys "^w"   # Ctrl+W

    # 5. Observe. Screenshot and READ the png — don't trust a blind capture.
    #    Window title and settings.json are also cheap assertions:
    Save-WindowScreenshot -Process $p -Path "$env:TEMP\after.png"
    $p.Refresh(); $p.MainWindowTitle
}
finally {
    # 6. Tear down: kill the app FIRST (it saves settings on exit and would
    #    overwrite the restore), then put the user's settings back.
    if ($p -and -not $p.HasExited) { Stop-Process -Id $p.Id -Confirm:$false; Start-Sleep 1 }
    Restore-EditorSettings
}
```

## What to verify, and how

| To check… | Do… |
|---|---|
| A menu item exists / its position / enabled state | `Invoke-ElementClick` the top-level menu, then `Get-MenuItemStates` (items come back in visual order) |
| A keyboard shortcut | `Send-EditorKeys -Keys "^w"` etc. — dispatch lives in `MainWindow.axaml.cs` `OnKeyDownTunnel` |
| Title / project open state | `$p.Refresh(); $p.MainWindowTitle` — the title carries `[ProjectName]` and a `●` dirty marker |
| Persisted state changes | Read `%LOCALAPPDATA%\PillarsDialogEditor\settings.json` after the action |
| Anything visual | `Save-WindowScreenshot`, then **Read the png and look at it**; send before/after shots to the user via SendUserFile |

## Rules of the road

- **Never run against the user's real project/settings.** Backup → scratch
  project → restore, every time. The `finally` block is not optional.
- **Kill the app before restoring settings** — it writes settings on exit.
- Dialogs that need the file picker (Open/Save As) are not automatable this
  way; arrange the state via settings.json + scratch files instead.
- One instance at a time: `Get-EditorWindow` finds the window by PID, but
  SendKeys goes to whatever is foreground.
- The UIA element names are the localised strings from
  `DialogEditor.Avalonia/Resources/Strings.axaml` (e.g. "Close Project",
  "Merge Projects…" — note the ellipsis character `…`, not three dots).
- Per CLAUDE.md's "UI Automation Support" rule, controls are expected to stay
  UIA-discoverable; if an element can't be found by Name, that's likely a
  defect worth surfacing, not something to work around silently.
