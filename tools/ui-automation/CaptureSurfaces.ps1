# CaptureSurfaces.ps1 — repeatable screenshot matrices of the Dialog Editor
# (theme × font scale), for look-and-feel reviews and before/after PRs.
#
# Dot-source from pwsh 7+ (it dot-sources DriveApp.ps1 itself):
#     . "tools\ui-automation\CaptureSurfaces.ps1"
#
# Ready-made captures built on this library:
#     Capture-Canvas.ps1             — canvas, as opened + fit-to-screen (#82)
#     Capture-ConversationsDock.ps1  — Conversations dock, collapsed + expanded (#85)
#
# Why these helpers exist (learned while capturing for #82/#85):
#   * A capture run changes the user's settings.json. Invoke-CaptureMatrix always
#     does Backup-EditorSettings → run → kill app → Restore-EditorSettings, even
#     on failure.
#   * settings.json can carry PendingRestores (files the editor will put back
#     into the GAME FOLDER on next launch). A screenshot run must never trigger
#     that, so Set-CaptureSettings clears them in the temporary copy only; the
#     restore puts the user's list back untouched.
#   * Opening a game folder with no BackupPaths entry starts the first-run
#     backup flow (a modal prompt, then a large copy). Set-CaptureSettings points
#     such folders at an empty scratch directory in the temporary copy, so the
#     run stays read-only and unattended.
#   * ShowWindow(SW_MAXIMIZE) sent before the window exists silently does
#     nothing. Wait-EditorMaximized retries until IsZoomed confirms it — an
#     unmaximized run produces differently-framed shots with desktop at the edges.
#   * Number formatting in file names uses the invariant culture: under a
#     Danish (or any comma-decimal) locale "{0:0.00}" gives "1,50".
#
# Only local desktop input and accessibility APIs are used — see the security
# note in DriveApp.ps1 and the "UI Automation Support" rule in CLAUDE.md.

. (Join-Path $PSScriptRoot 'DriveApp.ps1')

$script:CaptureThemes = @('Dark', 'Light', 'Colourblind', 'HighContrast')

function Initialize-CaptureSurfaces {
    Initialize-DriveApp
    if (-not ('CaptureWin32' -as [type])) {
        Add-Type @"
using System;
using System.Runtime.InteropServices;
public class CaptureWin32 {
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr hWnd);
}
"@
    }
}

function Format-CaptureTag {
    # "dark-1.50x" — stable, sortable, locale-independent.
    param([Parameter(Mandatory)][string]$Theme, [Parameter(Mandatory)][double]$FontScale)
    '{0}-{1}x' -f $Theme.ToLowerInvariant(), $FontScale.ToString('0.00', [cultureinfo]::InvariantCulture)
}

function Set-CaptureSettings {
    # Rewrite settings.json for one capture configuration. Call only between
    # Backup-EditorSettings and Restore-EditorSettings (Invoke-CaptureMatrix does).
    param(
        [Parameter(Mandatory)][ValidateScript({ $_ -in $script:CaptureThemes })][string]$Theme,
        [Parameter(Mandatory)][double]$FontScale,
        [string]$GameDirectory,          # $null → keep the user's LastGameDirectory
        [string]$ProjectPath = '',       # '' → start projectless (read-only browsing)
        [Parameter(Mandatory)][string]$ScratchDirectory
    )
    $path = Join-Path $env:LOCALAPPDATA 'PillarsDialogEditor\settings.json'
    $s = Get-Content $path -Raw | ConvertFrom-Json

    $s.Theme           = $Theme
    $s.FontScale       = $FontScale
    $s.LastProjectPath = $ProjectPath
    $s.PendingRestores = @()   # never let a capture run write to the game folder

    if ($GameDirectory) {
        if (-not $GameDirectory.EndsWith('\')) { $GameDirectory += '\' }
        $s.LastGameDirectory = $GameDirectory
        $known = @($s.KnownGameDirectories) | Where-Object { $_ }
        if ($GameDirectory -notin $known) { $s.KnownGameDirectories = @($known) + $GameDirectory }
    }

    # Suppress the first-run backup flow for any folder that has never been backed up.
    $game = $s.LastGameDirectory
    if ($game -and -not $s.BackupPaths.PSObject.Properties[$game]) {
        $fake = Join-Path $ScratchDirectory 'no-backup'
        New-Item -ItemType Directory -Force $fake | Out-Null
        $s.BackupPaths | Add-Member -NotePropertyName $game -NotePropertyValue "$fake\"
    }

    $s | ConvertTo-Json -Depth 20 | Set-Content $path -Encoding utf8
}

function Wait-EditorMaximized {
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process, [int]$Attempts = 10)
    for ($i = 0; $i -lt $Attempts; $i++) {
        $Process.Refresh()
        if ($Process.MainWindowHandle -ne [IntPtr]::Zero) {
            [CaptureWin32]::ShowWindow($Process.MainWindowHandle, 3) | Out-Null   # SW_MAXIMIZE
            Start-Sleep -Seconds 2
            $Process.Refresh()
            if ([CaptureWin32]::IsZoomed($Process.MainWindowHandle)) { return }
        }
        Start-Sleep -Seconds 2
    }
    throw 'Wait-EditorMaximized: the editor window never reported maximized.'
}

function Get-ElementWindowRect {
    # An element's bounding box relative to the app window's top-left, i.e. in
    # the pixel space of a Save-WindowScreenshot image.
    param([Parameter(Mandatory)][System.Diagnostics.Process]$Process, [Parameter(Mandatory)]$Element)
    $Process.Refresh()
    $w = New-Object DriveAppWin32+RECT
    [DriveAppWin32]::GetWindowRect($Process.MainWindowHandle, [ref]$w) | Out-Null
    $b = $Element.Current.BoundingRectangle
    [pscustomobject]@{
        X = [int]($b.X - $w.Left); Y = [int]($b.Y - $w.Top)
        Width = [int]$b.Width;     Height = [int]$b.Height
    }
}

function Save-ImageCrop {
    # Crop a PNG to a rectangle (clamped to the image). -Width/-Height 0 = to the edge.
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination,
        [int]$X = 0, [int]$Y = 0, [int]$Width = 0, [int]$Height = 0
    )
    $img = [System.Drawing.Image]::FromFile($Source)
    try {
        $x = [Math]::Max(0, [Math]::Min($X, $img.Width - 1))
        $y = [Math]::Max(0, [Math]::Min($Y, $img.Height - 1))
        $w = if ($Width  -gt 0) { [Math]::Min($Width,  $img.Width  - $x) } else { $img.Width  - $x }
        $h = if ($Height -gt 0) { [Math]::Min($Height, $img.Height - $y) } else { $img.Height - $y }
        $rect = New-Object System.Drawing.Rectangle $x, $y, $w, $h
        $bmp = ([System.Drawing.Bitmap]$img).Clone($rect, $img.PixelFormat)
        try { $bmp.Save($Destination, [System.Drawing.Imaging.ImageFormat]::Png) } finally { $bmp.Dispose() }
    }
    finally { $img.Dispose() }
}

function Find-EditorElements {
    # All descendants matching a Name and/or ControlType (unlike Find-EditorElement,
    # never throws on 0 or >1 matches — callers pick what they need).
    param([Parameter(Mandatory)]$Window, [string]$Name, [System.Windows.Automation.ControlType]$ControlType)
    $conds = @()
    if ($Name)        { $conds += New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::NameProperty, $Name) }
    if ($ControlType) { $conds += New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, $ControlType) }
    $cond = if ($conds.Count -eq 1) { $conds[0] } else { New-Object System.Windows.Automation.AndCondition($conds) }
    return ,@($Window.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond))
}

function Invoke-ElementCenterClick {
    param([Parameter(Mandatory)]$Element, [int]$SettleMs = 800)
    $pt = $Element.GetClickablePoint()
    [DriveAppWin32]::Click([int]$pt.X, [int]$pt.Y)
    Start-Sleep -Milliseconds $SettleMs
}

function Open-BrowserConversation {
    # Filter the Conversations dock to one conversation and open it on the canvas.
    param(
        [Parameter(Mandatory)][System.Diagnostics.Process]$Process,
        [Parameter(Mandatory)]$Window,
        [Parameter(Mandatory)][string]$Conversation,
        [int]$TimeoutSeconds = 10
    )
    # The filter box's UIA name is its localised placeholder (Placeholder_FilterConversations).
    $filter = (Find-EditorElements -Window $Window -Name 'Filter conversations…' -ControlType ([System.Windows.Automation.ControlType]::Edit)) | Select-Object -First 1
    if (-not $filter) { throw "Open-BrowserConversation: filter box not found." }
    Invoke-ElementCenterClick $filter
    # SendKeys treats + ^ % ~ ( ) { } [ ] specially; conversation names are
    # [a-z0-9_] in both games, so no escaping is needed in practice.
    Send-EditorKeys -Process $Process -Keys $Conversation -SettleMs 1500

    $item = $null
    for ($i = 0; $i -lt $TimeoutSeconds -and -not $item; $i++) {
        $item = (Find-EditorElements -Window $Window -Name $Conversation -ControlType ([System.Windows.Automation.ControlType]::TreeItem)) | Select-Object -First 1
        if (-not $item) { Start-Sleep -Seconds 1 }
    }
    if (-not $item) { throw "Open-BrowserConversation: '$Conversation' not found in the Conversations dock." }
    Invoke-ElementCenterClick $item
    Start-Sleep -Seconds 4   # let the canvas lay out the conversation
}

function Invoke-CaptureMatrix {
    # Launch the editor once per theme × font scale, maximize it, and hand the
    # running app to -Capture. Settings are backed up first and restored last,
    # whatever happens; the app is killed before the restore (it saves on exit).
    #
    # -Capture receives: $Process, $Window, $Tag ("dark-1.50x"), $OutDir.
    param(
        [Parameter(Mandatory)][string]$RepoRoot,
        [Parameter(Mandatory)][string]$OutDir,
        [Parameter(Mandatory)][scriptblock]$Capture,
        [string[]]$Themes = @('Dark', 'Light'),
        [double[]]$FontScales = @(1.0, 1.5, 2.0),
        [string]$GameDirectory,
        [string]$ProjectPath = '',
        [int]$StartupSeconds = 9
    )
    Initialize-CaptureSurfaces
    if (Get-Process DialogEditor.Avalonia -ErrorAction SilentlyContinue) {
        throw 'The Dialog Editor is already running. Close it first: SendKeys goes to the foreground window, and a running instance would overwrite settings.json on exit.'
    }
    New-Item -ItemType Directory -Force $OutDir | Out-Null
    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) "PillarsDialogEditor.capture"
    New-Item -ItemType Directory -Force $scratch | Out-Null

    Backup-EditorSettings
    try {
        foreach ($theme in $Themes) {
            foreach ($scale in $FontScales) {
                Set-CaptureSettings -Theme $theme -FontScale $scale -GameDirectory $GameDirectory `
                                    -ProjectPath $ProjectPath -ScratchDirectory $scratch
                $p = Start-DialogEditor -RepoRoot $RepoRoot -StartupSeconds $StartupSeconds
                try {
                    Wait-EditorMaximized -Process $p
                    Start-Sleep -Seconds 2
                    $win = Get-EditorWindow -Process $p
                    $tag = Format-CaptureTag -Theme $theme -FontScale $scale
                    & $Capture $p $win $tag $OutDir
                    Write-Host "captured $tag"
                }
                finally {
                    if (-not $p.HasExited) { Stop-Process -Id $p.Id -Confirm:$false; Start-Sleep -Seconds 2 }
                }
            }
        }
    }
    finally {
        Restore-EditorSettings
    }
}
