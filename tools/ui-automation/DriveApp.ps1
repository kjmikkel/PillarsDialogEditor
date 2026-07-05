# DriveApp.ps1 — UI-automation helpers for end-to-end verification of the
# Pillars Dialog Editor (Avalonia, Windows).
#
# Dot-source this file from pwsh 7+ (NOT Windows PowerShell 5.1):
#     . "tools\ui-automation\DriveApp.ps1"
#
# Why these helpers exist (hard-won specifics, don't rediscover them):
#   * Avalonia exposes UI Automation (UIA), so menus/controls are discoverable
#     by Name — but its TOP-LEVEL MenuItems do NOT support the UIA
#     ExpandCollapse pattern. Opening a menu requires a real mouse click at the
#     item's clickable point (see Invoke-ElementClick).
#   * pwsh 7 can load the WPF UIA client assemblies (UIAutomationClient /
#     UIAutomationTypes) because the .NET Desktop runtime ships them; this is
#     what Initialize-DriveApp does.
#   * SendKeys shortcuts only reach the app when its window is foreground —
#     call Set-EditorForeground first.
#   * The app persists state in %LOCALAPPDATA%\PillarsDialogEditor\settings.json
#     (LastProjectPath drives auto-reopen on startup). ALWAYS Backup-EditorSettings
#     before mutating it and Restore-EditorSettings in a finally — otherwise a
#     verification run clobbers the user's real session.
#   * Scratch .dialogproject files must be written through the app's own
#     DialogProjectSerializer (New-ScratchProject) — hand-rolled JSON risks a
#     silent shape mismatch with the serializer options.
#
# Security note (see CLAUDE.md "UI Automation Support"): these helpers drive the
# app purely from the OUTSIDE via OS-level accessibility APIs and input
# injection on the local desktop. The app itself contains no remote-control or
# test-hook endpoint, and none should be added on its account.

# No Set-StrictMode here: this file is dot-sourced, so strict mode would leak
# into the calling session and break unrelated code that reads unset variables.

$script:SettingsPath = Join-Path $env:LOCALAPPDATA "PillarsDialogEditor\settings.json"
$script:SettingsBackup = $null

function Initialize-DriveApp {
    # Loads the UIA client, WinForms (SendKeys), Drawing (screenshots), and the
    # small Win32 shim used for clicks/foreground/window-rect. Idempotent.
    Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing
    if (-not ("DriveAppWin32" -as [type])) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public class DriveAppWin32 {
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT r);
    [DllImport("user32.dll")] public static extern void SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    public struct RECT { public int Left, Top, Right, Bottom; }
    // A real click (down+up) at screen coordinates. UIA Invoke/ExpandCollapse
    // patterns are not implemented on Avalonia's top-level menu items, so
    // synthetic mouse input is the reliable way to open menus.
    public static void Click(int x, int y) {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);   // left down
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);   // left up
    }
}
"@
    }
}

# ── Settings lifecycle ────────────────────────────────────────────────────────

function Backup-EditorSettings {
    # Snapshot the user's real settings before a verification run touches them.
    if (Test-Path $script:SettingsPath) {
        $script:SettingsBackup = Get-Content $script:SettingsPath -Raw
    }
}

function Restore-EditorSettings {
    # Put the user's settings back exactly as they were. Call from a finally.
    if ($null -ne $script:SettingsBackup) {
        Set-Content -Path $script:SettingsPath -Value $script:SettingsBackup -Encoding UTF8
    }
}

function Set-EditorLastProject {
    # Point LastProjectPath at a project so the app auto-opens it on launch —
    # the easiest way to start a GUI run "with a project open" without driving
    # the file-picker dialog. Pass $null/'' to start projectless.
    param([string]$ProjectPath)
    $json = Get-Content $script:SettingsPath -Raw | ConvertFrom-Json
    $json.LastProjectPath = $ProjectPath
    $json | ConvertTo-Json -Depth 10 | Set-Content $script:SettingsPath -Encoding UTF8
}

function New-ScratchProject {
    # Write a valid empty .dialogproject through the app's own serializer so the
    # file always matches the current schema. Requires a Debug build.
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$Path,
        [string]$Name = "ScratchProject"
    )
    Add-Type -Path "$RepoRoot\DialogEditor.Core\bin\Debug\net8.0\DialogEditor.Core.dll"
    Add-Type -Path "$RepoRoot\DialogEditor.Patch\bin\Debug\net8.0\DialogEditor.Patch.dll"
    $empty = [DialogEditor.Patch.DialogProject]::Empty($Name)
    [DialogEditor.Patch.DialogProjectSerializer]::SaveToFile($Path, $empty)
}

# ── App lifecycle ─────────────────────────────────────────────────────────────

function Start-DialogEditor {
    # Launches the built exe and waits for the main window. Returns the Process.
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [int]$StartupSeconds = 6
    )
    $exe = "$RepoRoot\DialogEditor.Avalonia\bin\Debug\net8.0\DialogEditor.Avalonia.exe"
    if (-not (Test-Path $exe)) { throw "Not built: $exe — run 'dotnet build' first." }
    $p = Start-Process $exe -PassThru
    Start-Sleep -Seconds $StartupSeconds
    $p.Refresh()
    if ($p.HasExited) { throw "Dialog Editor exited during startup." }
    return $p
}

function Set-EditorForeground {
    # SendKeys and screenshots need the window frontmost.
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)
    [DriveAppWin32]::SetForegroundWindow($Process.MainWindowHandle) | Out-Null
    Start-Sleep -Milliseconds 400
}

# ── UIA queries and interaction ───────────────────────────────────────────────

function Get-EditorWindow {
    # The app's main window as a UIA AutomationElement.
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process)
    $root = [System.Windows.Automation.AutomationElement]::RootElement
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ProcessIdProperty, $Process.Id)
    $win = $root.FindFirst([System.Windows.Automation.TreeScope]::Children, $cond)
    if ($null -eq $win) { throw "No UIA window for PID $($Process.Id)." }
    return $win
}

function Invoke-ElementClick {
    # Click a UIA element by Name via real mouse input (see file header for why
    # this is a click and not ExpandCollapse/Invoke). Use it to open menus:
    #   Invoke-ElementClick -Window $win -Name "File"
    param(
        [Parameter(Mandatory)]$Window,
        [Parameter(Mandatory)][string]$Name,
        [int]$SettleMs = 800
    )
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $el = $Window.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
    if ($null -eq $el) { throw "UIA element named '$Name' not found." }
    $pt = $el.GetClickablePoint()
    [DriveAppWin32]::Click([int]$pt.X, [int]$pt.Y)
    Start-Sleep -Milliseconds $SettleMs
}

function Get-MenuItemStates {
    # All MenuItem elements under the window as "Name | enabled=…" strings —
    # with a menu popup open this includes its items, so it verifies both
    # placement (order) and CanExecute-driven enablement in one call.
    param([Parameter(Mandatory)]$Window)
    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem)
    foreach ($it in $Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)) {
        "{0} | enabled={1}" -f $it.Current.Name, $it.Current.IsEnabled
    }
}

function Send-EditorKeys {
    # SendKeys syntax: "^w" = Ctrl+W, "{ESC}" = Escape, "+^s" = Ctrl+Shift+S.
    # Brings the app foreground first so the input lands in the right window.
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string]$Keys,
        [int]$SettleMs = 800
    )
    Set-EditorForeground -Process $Process
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds $SettleMs
}

function Save-WindowScreenshot {
    # PNG of the app window. LOOK at the result (Read the file) — a blank frame
    # means the window wasn't frontmost or hadn't rendered yet.
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)][string]$Path
    )
    Set-EditorForeground -Process $Process
    $r = New-Object DriveAppWin32+RECT
    [DriveAppWin32]::GetWindowRect($Process.MainWindowHandle, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($r.Left, $r.Top, 0, 0, $bmp.Size)
    $bmp.Save($Path)
    $g.Dispose(); $bmp.Dispose()
}
