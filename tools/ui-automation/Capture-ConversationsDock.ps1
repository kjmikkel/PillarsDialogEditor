<#
.SYNOPSIS
    Screenshot the unfiltered Conversations dock across themes × font scales (#85).

.DESCRIPTION
    Opens a throwaway project containing one new conversation (so the "(new)"
    folder shows), then for each configuration saves:
      <tag>-collapsed.png / <tag>-expanded.png           — cropped to the dock
      <tag>-collapsed-full.png / <tag>-expanded-full.png — the whole window
    "Expanded" opens the first -ExpandFolders top-level folders through the UIA
    ExpandCollapse pattern (the tree then scrolls into mid-list rows).
    Needs a Debug build and a real interactive desktop; close any running editor
    first. The user's settings are restored afterwards.

.EXAMPLE
    ./tools/ui-automation/Capture-ConversationsDock.ps1 -OutDir $env:TEMP\dock-shots
.EXAMPLE
    ./tools/ui-automation/Capture-ConversationsDock.ps1 -OutDir poe1 -GameDirectory 'X:\Games\PillarsOfEternity' `
        -Themes Dark,Light,HighContrast
#>
param(
    [Parameter(Mandatory)][string]$OutDir,
    [string]$GameDirectory,   # default: the game folder the editor last opened
    [string[]]$Themes = @('Dark', 'Light', 'HighContrast'),
    [double[]]$FontScales = @(1.0, 1.5, 2.0),
    [int]$ExpandFolders = 4
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'CaptureSurfaces.ps1')
$repo = Resolve-Path (Join-Path $PSScriptRoot '..\..')

# Throwaway project written through the app's own serializer (see New-ScratchProject).
$project = Join-Path ([System.IO.Path]::GetTempPath()) 'CaptureDock.dialogproject'
Add-Type -Path "$repo\DialogEditor.Core\bin\Debug\net8.0\DialogEditor.Core.dll"
Add-Type -Path "$repo\DialogEditor.Patch\bin\Debug\net8.0\DialogEditor.Patch.dll"
$p0 = [DialogEditor.Patch.DialogProject]::Empty('CaptureDock').WithNewConversation('my_new_conversation')
[DialogEditor.Patch.DialogProjectSerializer]::SaveToFile($project, $p0)

try {
    Invoke-CaptureMatrix -RepoRoot $repo -OutDir $OutDir -Themes $Themes -FontScales $FontScales `
                         -GameDirectory $GameDirectory -ProjectPath $project -Capture {
        param($p, $win, $tag, $out)

        $tree = (Find-EditorElements -Window $win -ControlType ([System.Windows.Automation.ControlType]::Tree)) | Select-Object -First 1
        if (-not $tree) { throw 'Conversations tree not found.' }

        function Save-DockShots([string]$state) {
            $full = Join-Path $out "$tag-$state-full.png"
            Save-WindowScreenshot -Process $p -Path $full
            # Crop from the window's left edge to just past the tree's right edge:
            # that is the whole dock (header, filter, tree, footer) at any dock width.
            $r = Get-ElementWindowRect -Process $p -Element $tree
            Save-ImageCrop -Source $full -Destination (Join-Path $out "$tag-$state.png") -Width ($r.X + $r.Width + 12)
        }

        Save-DockShots 'collapsed'

        $walker = [System.Windows.Automation.TreeWalker]::ControlViewWalker
        $child = $walker.GetFirstChild($tree); $expanded = 0
        while ($child -and $expanded -lt $ExpandFolders) {
            $pattern = $null
            if ($child.TryGetCurrentPattern([System.Windows.Automation.ExpandCollapsePattern]::Pattern, [ref]$pattern)) {
                $pattern.Expand(); $expanded++; Start-Sleep -Milliseconds 300
            }
            $child = $walker.GetNextSibling($child)
        }
        Start-Sleep -Seconds 1
        Save-DockShots 'expanded'
    }
}
finally {
    Remove-Item $project -ErrorAction SilentlyContinue -Confirm:$false
}
