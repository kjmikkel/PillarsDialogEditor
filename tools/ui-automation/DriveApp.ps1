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
#     (Re-verified 2026-09-03 via the MCP server's read_tree: the app's top-level
#     MenuItems expose ScrollItem ONLY. The ExpandCollapse that does show up on a
#     MenuItem belongs to the title bar's OS "System" item, NOT to File/Edit/View/
#     Test/Help — so this note is correct, do not "fix" it.)
#   * Synthetic clicking is STATEFUL, unlike invoking a pattern: if a previous step left
#     a menu popup open, the next click merely dismisses that popup instead of opening
#     the menu you asked for. Send {ESC} before starting a menu interaction.
#   * SetForegroundWindow DISMISSES an open popup, so do not call Set-EditorForeground
#     between opening a menu and clicking an item in it.
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
$script:SettingsBackupPath = Join-Path $env:TEMP "PillarsDialogEditor.settings.backup.json"

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
    #
    # The snapshot goes to a FILE, not just a variable: a verification run is often
    # split across several pwsh invocations (launch in one, drive in the next, tear
    # down in a third), and $script: state dies with the session that set it. An
    # in-memory-only backup makes Restore-EditorSettings a silent no-op in any later
    # session — the user's real LastProjectPath then keeps whatever the run left behind.
    #
    # An existing backup is never overwritten: if a previous run died before restoring,
    # the older file is the one holding the user's genuine settings.
    if (-not (Test-Path $script:SettingsPath)) { return }
    if (-not (Test-Path $script:SettingsBackupPath)) {
        Copy-Item $script:SettingsPath $script:SettingsBackupPath -Force
    }
    $script:SettingsBackup = Get-Content $script:SettingsPath -Raw
}

function Restore-EditorSettings {
    # Put the user's settings back exactly as they were. Call from a finally.
    # Kill the app FIRST — it rewrites settings on exit and would win the race.
    # Reads the on-disk snapshot, so this works from a different pwsh session than
    # the one that called Backup-EditorSettings. Removes the snapshot on success so
    # the next run starts clean; warns loudly if there is nothing to restore, since
    # a silent no-op here is how a run leaks its state into the user's session.
    if (Test-Path $script:SettingsBackupPath) {
        Copy-Item $script:SettingsBackupPath $script:SettingsPath -Force
        Remove-Item $script:SettingsBackupPath -Force
    }
    elseif ($null -ne $script:SettingsBackup) {
        Set-Content -Path $script:SettingsPath -Value $script:SettingsBackup -Encoding UTF8
    }
    else {
        Write-Warning "Restore-EditorSettings: no backup found — settings.json still holds whatever this run wrote. Call Backup-EditorSettings before mutating it."
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
    # All MenuItem elements UNDER THE APP'S OWN MENU as "Name | enabled=…" strings.
    #
    # Scoping matters: a window-wide ControlType=MenuItem search also returns the title
    # bar's system menu ("System", AutomationId 'Item 1'), which is indistinguishable from
    # File/Edit/View/Test/Help by control type. The 2026-09-03 audit's first probe
    # enumerated that way, clicked "System", opened the OS window menu and wedged the run —
    # the main-window walk collapsed to 12 elements and every later lookup failed.
    #
    # The menu bar is addressed by AutomationId. It used to be anonymous, leaving ClassName
    # as the only handle; issue #15 finding 5 gave it 'MainMenu'.
    param([Parameter(Mandatory)]$Window)

    $menuCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::AutomationIdProperty, 'MainMenu')
    $appMenu = $Window.FindFirst(
        [System.Windows.Automation.TreeScope]::Descendants, $menuCond)
    if ($null -eq $appMenu) { throw "App menu bar not found (AutomationId='MainMenu')." }

    $itemCond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
        [System.Windows.Automation.ControlType]::MenuItem)

    # Stream the strings rather than returning `,$out`. The comma idiom exists to stop an
    # EMPTY array collapsing to $null, but applied to a non-empty array it NESTS it, so
    # @(Get-MenuItemStates ...) then yields one element containing all the rows. Callers
    # should wrap in @() — which also turns the empty case into an empty array.
    foreach ($it in $appMenu.FindAll(
            [System.Windows.Automation.TreeScope]::Descendants, $itemCond)) {
        "{0} | enabled={1}" -f $it.Current.Name, $it.Current.IsEnabled
    }
}

function Find-EditorElement {
    # Find ONE element, erroring when the match is ambiguous instead of taking tree order.
    #
    # Invoke-ElementClick uses FindFirst on Name, which silently takes the first match in
    # tree order — and the 2026-09-03 audit confirmed eight same-surface Name collisions,
    # so that can act on the wrong control and still look successful. Filtering on
    # ControlType.Edit was the old workaround; it is coincidental, and does not help for
    # e.g. the "Language:" label colliding with its ComboBox (a ComboBox, not an Edit).
    #
    # Pass -ControlType and/or -WithinPane to narrow. Panes are reliably named
    # (LeftPane / Documents / RightPane), so pane scoping is the dependable disambiguator.
    param(
        [Parameter(Mandatory)]$Window,
        [Parameter(Mandatory)][string]$Name,
        [string]$ControlType,
        [string]$WithinPane
    )

    $scope = $Window
    if ($WithinPane) {
        $paneCond = New-Object System.Windows.Automation.PropertyCondition(
            [System.Windows.Automation.AutomationElement]::NameProperty, $WithinPane)
        $scope = $Window.FindFirst(
            [System.Windows.Automation.TreeScope]::Descendants, $paneCond)
        if ($null -eq $scope) { throw "Pane '$WithinPane' not found." }
    }

    $cond = New-Object System.Windows.Automation.PropertyCondition(
        [System.Windows.Automation.AutomationElement]::NameProperty, $Name)
    $found = @($scope.FindAll(
        [System.Windows.Automation.TreeScope]::Descendants, $cond))

    if ($ControlType) {
        $found = @($found | Where-Object {
            $_.Current.ControlType.ProgrammaticName -eq "ControlType.$ControlType" })
    }

    if ($found.Count -eq 0) { throw "No element named '$Name' found." }
    if ($found.Count -gt 1) {
        $desc = ($found | ForEach-Object {
            "[{0}] id='{1}'" -f ($_.Current.ControlType.ProgrammaticName -replace '^ControlType\.', ''),
                                $_.Current.AutomationId }) -join '; '
        throw "'$Name' is ambiguous ($($found.Count) matches): $desc. Narrow with -ControlType or -WithinPane."
    }
    return $found[0]
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
