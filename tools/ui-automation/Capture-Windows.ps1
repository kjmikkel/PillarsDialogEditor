<#
.SYNOPSIS
    Screenshot the editor's docks, panes and secondary windows across themes × font scales (#86).

.DESCRIPTION
    Opens the PoE2 canonical mod project (DialogEditor.Tests/Fixtures/CanonicalMod/poe2) against
    a temporary copy of the canonical PoE2 game fixture, opens the `canonical` conversation,
    selects a node, and then opens each surface in turn through the app's own menus, shortcuts
    and buttons. Each surface is saved as <tag>-<surface>.png; secondary windows are cropped to
    the window, docks to the dock. A surface that will not open is recorded in
    <OutDir>/failures.txt and the run carries on.

    The project folder is a scratch copy with a two-commit git history, so History, Compare
    Versions, Attribution and Branches have something to show.

    Needs a Debug build, git on PATH and a real interactive desktop that nobody is using: the
    run clicks and types into the foreground window. Close any running editor first. The
    user's settings are restored afterwards.

.EXAMPLE
    ./tools/ui-automation/Capture-Windows.ps1 -OutDir $env:TEMP\windows
.EXAMPLE
    ./tools/ui-automation/Capture-Windows.ps1 -OutDir $env:TEMP\windows -Only settings,about -Themes Dark -FontScales 2.0
#>
param(
    [Parameter(Mandatory)][string]$OutDir,
    [string[]]$Themes = @('Dark', 'Light', 'HighContrast'),
    [double[]]$FontScales = @(1.0, 2.0),
    [string[]]$Only   # surface names to capture; default all
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CaptureSurfaces.ps1')
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

# UIA names are the localised strings, so look them up by key rather than hard-coding the
# English text: a reworded menu item must not silently break the run.
$strings = @{}
foreach ($file in "$repo\DialogEditor.Avalonia\Resources\Strings.axaml",
                  "$repo\DialogEditor.Avalonia.Shared\Resources\SharedStrings.axaml") {
    [xml]$stringsXml = Get-Content $file -Raw
    foreach ($n in $stringsXml.DocumentElement.ChildNodes) {
        if ($n.NodeType -eq 'Element' -and $n.Attributes) {
            $k = $n.GetAttribute('Key', 'http://schemas.microsoft.com/winfx/2006/xaml')
            if ($k) { $strings[$k] = $n.InnerText }
        }
    }
}
function L([string]$key) {
    if (-not $strings.ContainsKey($key)) { throw "No string '$key' in Strings.axaml or SharedStrings.axaml." }
    $strings[$key]
}

# ── Scratch workspace: game copy + project with git history ─────────────────────
$work = Join-Path ([System.IO.Path]::GetTempPath()) 'PillarsDialogEditor.capture-windows'
if (Test-Path $work) { Remove-Item -Recurse -Force $work -Confirm:$false }
$game = Join-Path $work 'game'
$projDir = Join-Path $work 'project'
New-Item -ItemType Directory -Force $game, $projDir | Out-Null
Copy-Item -Recurse "$repo\DialogEditor.Tests\Fixtures\Canonical\poe2\*" $game
$project = Join-Path $projDir 'canonical_mod.dialogproject'
Copy-Item "$repo\DialogEditor.Tests\Fixtures\CanonicalMod\poe2\canonical_mod.dialogproject" $project
Push-Location $projDir
try {
    git init -q; git config user.email capture@localhost; git config user.name Capture
    git add -A; git commit -q -m 'First version'
    Add-Content $project ''   # any change will do: the second commit gives History and Diff two versions
    git commit -q -am 'Second version'
}
finally { Pop-Location }

# ── Helpers ────────────────────────────────────────────────────────────────────
$Text = [System.Windows.Automation.ControlType]::Text
$Btn  = [System.Windows.Automation.ControlType]::Button

function Invoke-Menu($p, $win, [string[]]$path) {
    # Top-level menu items need a real click (no UIA ExpandCollapse); submenu items are
    # found under the main window once the popup is open.
    Invoke-ElementClick -Window $win -Name $path[0]
    foreach ($item in $path[1..($path.Count - 1)]) {
        $el = (Find-EditorElements -Window $win -Name $item -ControlType ([System.Windows.Automation.ControlType]::MenuItem)) | Select-Object -First 1
        if (-not $el) { throw "Menu item '$item' not found." }
        Invoke-ElementCenterClick $el
    }
}

function Close-Secondary($p, $dlg) {
    $wp = $null
    if ($dlg.TryGetCurrentPattern([System.Windows.Automation.WindowPattern]::Pattern, [ref]$wp)) { $wp.Close() }
    else { Send-EditorKeys -Process $p -Keys '{ESC}' }
    Start-Sleep -Milliseconds 800
}

function Hide-Pointer($p) {
    # Park the pointer on the main window's title bar so no tooltip covers a capture.
    $r = New-Object DriveAppWin32+RECT
    [DriveAppWin32]::GetWindowRect([IntPtr]$script:EditorMainHandles[$p.Id], [ref]$r) | Out-Null
    [DriveAppWin32]::SetCursorPos([int](($r.Left + $r.Right) / 2), $r.Top + 12)
    Start-Sleep -Milliseconds 600
}

function Save-Secondary($p, $out, $tag, $name, [string]$title) {
    $dlg = Wait-EditorSecondaryWindow -Process $p -Name $title
    Hide-Pointer $p
    Save-ElementScreenshot -Element $dlg -Path (Join-Path $out "$tag-$name.png")
    Close-Secondary $p $dlg
}

function Select-AnyNode($win) {
    # Node cards carry no UIA name of their own; their footer text ("No conditions") does.
    # Prefer an on-screen node that HAS conditions, so the Condition Editor opens with content.
    $withConditions = (L 'Node_ConditionPlural') -replace '\{0\}.*$', ''   # "⚙ "
    $onScreen = @((Find-EditorElements -Window $win -ControlType $Text) | Where-Object { -not $_.Current.IsOffscreen })
    $footer = @($onScreen | Where-Object { $_.Current.Name.StartsWith($withConditions) }) +
              @($onScreen | Where-Object { $_.Current.Name.StartsWith((L 'Node_NoConditions')) }) |
              Select-Object -First 1
    if (-not $footer) { throw 'No node footer found on the canvas.' }
    # Canvas text reports no clickable point, so click the middle of its box instead.
    $b = $footer.Current.BoundingRectangle
    [DriveAppWin32]::Click([int]($b.X + $b.Width / 2), [int]($b.Y + $b.Height / 2))
    Start-Sleep -Milliseconds 1200
}

function Expand-NodeDetailGroups($win) {
    # Node Details opens with every group collapsed; the review wants them open, and the
    # Edit Conditions / Edit Scripts / VO import buttons live inside them.
    foreach ($key in 'Label_GroupIdentity', 'Label_GroupDisplay', 'Label_GroupVoice', 'Label_GroupLogic') {
        $el = (Find-EditorElements -Window $win -Name (L $key)) | Select-Object -First 1
        $pattern = $null
        if ($el -and $el.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) {
            if ($pattern.Current.ExpandCollapseState -ne 'Expanded') { $pattern.Expand(); Start-Sleep -Milliseconds 300 }
        }
    }
    Start-Sleep -Milliseconds 600
}

function Invoke-ByName($win, [string]$name) {
    # Invoke a button through UIA (no clickable point needed, works when scrolled out of view).
    # Find-EditorElements returns one wrapped array; the pipeline through ForEach unrolls it.
    $el = @(Find-EditorElements -Window $win -Name $name -ControlType $Btn | ForEach-Object { $_ }) | Select-Object -First 1
    if (-not $el) { throw "Button '$name' not found." }
    $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
    Start-Sleep -Milliseconds 800
}

# Each surface: Name, and Capture { param($p, $win, $tag, $out) }.
$surfaces = [ordered]@{
    'main'              = { param($p, $win, $tag, $out) Save-WindowScreenshot -Process $p -Path (Join-Path $out "$tag-main.png") }
    'settings'          = { param($p, $win, $tag, $out) Send-EditorKeys -Process $p -Keys '^,'; Save-Secondary $p $out $tag 'settings' $null }
    'find-replace'      = { param($p, $win, $tag, $out) Send-EditorKeys -Process $p -Keys '^h'; Save-Secondary $p $out $tag 'find-replace' $null }
    'batch-replace'     = { param($p, $win, $tag, $out) Send-EditorKeys -Process $p -Keys '^+h'; Save-Secondary $p $out $tag 'batch-replace' $null }
    'find-in-project'   = { param($p, $win, $tag, $out) Send-EditorKeys -Process $p -Keys '^+f'; Save-Secondary $p $out $tag 'find-in-project' $null }
    'flow-analytics'    = { param($p, $win, $tag, $out) Send-EditorKeys -Process $p -Keys '{F7}'; Save-Secondary $p $out $tag 'flow-analytics' $null }
    'speaker-lines'     = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Edit'), (L 'Menu_BrowseSpeakerLines')); Save-Secondary $p $out $tag 'speaker-lines' $null }
    'rep-disposition'   = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Edit'), (L 'Menu_RepDispositionBalance')); Save-Secondary $p $out $tag 'rep-disposition' $null }
    'compare-versions'  = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Edit'), (L 'Menu_CompareVersions')); Save-Secondary $p $out $tag 'compare-versions' $null }
    'history'           = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Edit'), (L 'Menu_History')); Save-Secondary $p $out $tag 'history' $null }
    'attribution'       = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Edit'), (L 'Menu_Attribution')); Save-Secondary $p $out $tag 'attribution' $null }
    'branches'          = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Edit'), (L 'Menu_Branches')); Save-Secondary $p $out $tag 'branches' $null }
    'patch-manager'     = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_File'), (L 'PatchManager_Title')); Save-Secondary $p $out $tag 'patch-manager' $null }
    'export'            = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_File'), (L 'Menu_ExportConversation')); Save-Secondary $p $out $tag 'export' $null }
    'validate-vo'       = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Test'), (L 'Menu_ValidateVO')); Save-Secondary $p $out $tag 'validate-vo' $null }
    'validate-tags'     = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Test'), (L 'Menu_ValidateTextTags')); Save-Secondary $p $out $tag 'validate-tags' $null }
    'tag-reference'     = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Help'), (L 'Menu_TagReference')); Save-Secondary $p $out $tag 'tag-reference' $null }
    'changelog'         = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Help'), (L 'Menu_Changelog')); Save-Secondary $p $out $tag 'changelog' $null }
    'about'             = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Help'), (L 'Menu_About')); Save-Secondary $p $out $tag 'about' $null }
    'legend'            = { param($p, $win, $tag, $out)
                            Invoke-ElementCenterClick ((Find-EditorElements -Window $win -Name (L 'AutomationName_ToggleHelp')) | Select-Object -First 1)
                            $dlg = Wait-EditorSecondaryWindow -Process $p
                            Save-ElementScreenshot -Element $dlg -Path (Join-Path $out "$tag-legend.png")
                            Invoke-ElementCenterClick ((Find-EditorElements -Window $win -Name (L 'AutomationName_ToggleHelp')) | Select-Object -First 1) }
    'node-details'      = { param($p, $win, $tag, $out) Select-AnyNode $win; Expand-NodeDetailGroups $win
                            Save-WindowScreenshot -Process $p -Path (Join-Path $out "$tag-node-details.png") }
    'condition-editor'  = { param($p, $win, $tag, $out)
                            Invoke-ByName $win (L 'AutomationName_EditConditions')
                            Save-Secondary $p $out $tag 'condition-editor' $null }
    'script-editor'     = { param($p, $win, $tag, $out)
                            Invoke-ByName $win (L 'AutomationName_EditScripts')
                            Save-Secondary $p $out $tag 'script-editor' $null }
    # VO import itself starts with a file picker, which UIA can't drive; the alias picker
    # opens directly. (VoImportDialog is reviewed from its .axaml only.)
    'vo-alias-picker'   = { param($p, $win, $tag, $out)
                            Invoke-ByName $win (L 'NodeDetail_ReuseVoButton')
                            Save-Secondary $p $out $tag 'vo-alias-picker' $null }
    'condition-search'  = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_View'), (L 'Menu_View_ConditionSearch')); Start-Sleep 1
                            Save-WindowScreenshot -Process $p -Path (Join-Path $out "$tag-condition-search.png") }
    'guided-tour'       = { param($p, $win, $tag, $out) Invoke-Menu $p $win @((L 'Menu_Help'), (L 'Menu_StartGuidedTour')); Start-Sleep 2
                            Save-WindowScreenshot -Process $p -Path (Join-Path $out "$tag-guided-tour.png")
                            Send-EditorKeys -Process $p -Keys '{ESC}' }
    'unsaved-changes'   = { param($p, $win, $tag, $out)
                            Invoke-ElementCenterClick ((Find-EditorElements -Window $win -Name (L 'AutomationName_AddAnnotation') -ControlType $Btn) | Select-Object -First 1) -SettleMs 1500
                            Save-WindowScreenshot -Process $p -Path (Join-Path $out "$tag-annotation.png")
                            Send-EditorKeys -Process $p -Keys '^w'
                            Save-Secondary $p $out $tag 'unsaved-changes' $null }
}

$failures = Join-Path $OutDir 'failures.txt'
New-Item -ItemType Directory -Force $OutDir | Out-Null
Set-Content $failures '' -NoNewline

Invoke-CaptureMatrix -RepoRoot $repo -OutDir $OutDir -Themes $Themes -FontScales $FontScales `
                     -GameDirectory $game -ProjectPath $project -StartupSeconds 12 -Capture {
    param($p, $win, $tag, $out)
    # The unsaved-changes surface leaves an edit behind and the matrix then kills the app,
    # so every launch after the first offers to restore the autosave. That dialog is a
    # surface worth reviewing too: capture it, then discard (the project is a scratch copy).
    try {
        $restore = Wait-EditorSecondaryWindow -Process $p -TimeoutSeconds 6
        $discard = (Find-EditorElements -Window $restore -Name (L 'AutosaveRestore_Discard') -ControlType $Btn) | Select-Object -First 1
        if ($discard) {
            Hide-Pointer $p
            Save-ElementScreenshot -Element $restore -Path (Join-Path $out "$tag-autosave-restore.png")
            $discard.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Seconds 1
        }
    }
    catch { }   # no restore prompt on this launch
    try { Open-BrowserConversation -Process $p -Window $win -Conversation 'canonical' }
    catch { Add-Content $failures "$tag open-conversation: $($_.Exception.Message)" }

    foreach ($name in $surfaces.Keys) {
        if ($Only -and $name -notin $Only) { continue }
        try {
            & $surfaces[$name] $p $win $tag $out
            Write-Host "  $tag $name"
        }
        catch {
            Add-Content $failures "$tag ${name}: $($_.Exception.Message)"
            Write-Host "  $tag $name FAILED: $($_.Exception.Message)"
            foreach ($w in @(Get-EditorSecondaryWindows -Process $p)) { Close-Secondary $p $w }   # don't let a stray dialog block the rest
            Send-EditorKeys -Process $p -Keys '{ESC}'
        }
    }
}
Write-Host "Done. Failures (if any): $failures"
