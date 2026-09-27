<#
.SYNOPSIS
    Screenshot a conversation on the canvas across themes × font scales (#82).

.DESCRIPTION
    For each configuration: opens -Conversation from the Conversations dock
    (projectless, read-only), saves "<tag>-opened.png", clicks Fit, and saves
    "<tag>-fit.png". Needs a Debug build and a real interactive desktop; close
    any running editor first. The user's settings are restored afterwards.

.EXAMPLE
    ./tools/ui-automation/Capture-Canvas.ps1 -OutDir $env:TEMP\canvas-shots
.EXAMPLE
    ./tools/ui-automation/Capture-Canvas.ps1 -OutDir shots -Conversation companion_cv_eder_intro `
        -GameDirectory 'X:\Games\PillarsOfEternity' -Themes Dark,HighContrast -FontScales 1,2
#>
param(
    [Parameter(Mandatory)][string]$OutDir,
    [string]$Conversation = 'companion_eder_hub',
    [string]$GameDirectory,   # default: the game folder the editor last opened
    [string[]]$Themes = @('Dark', 'Light'),
    [double[]]$FontScales = @(1.0, 1.5, 2.0)
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CaptureSurfaces.ps1')
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')

Invoke-CaptureMatrix -RepoRoot $repo -OutDir $OutDir -Themes $Themes -FontScales $FontScales `
                     -GameDirectory $GameDirectory -Capture {
    param($p, $win, $tag, $out)
    Open-BrowserConversation -Process $p -Window $win -Conversation $Conversation
    Save-WindowScreenshot -Process $p -Path (Join-Path $out "$tag-opened.png")

    # "Fit" is the canvas toolbar button's localised label (Legend_Control_Fit).
    $fit = (Find-EditorElements -Window $win -Name 'Fit' -ControlType ([System.Windows.Automation.ControlType]::Button)) | Select-Object -First 1
    if (-not $fit) { throw 'Fit button not found on the canvas toolbar.' }
    Invoke-ElementCenterClick $fit
    Start-Sleep -Seconds 2
    Save-WindowScreenshot -Process $p -Path (Join-Path $out "$tag-fit.png")
}
