<#
.SYNOPSIS
    Package source code for each distributable application into separate zip files.

.DESCRIPTION
    Copies the project folders required to build each app (excluding bin/, obj/,
    and other non-source artefacts), writes a trimmed solution file, and zips
    everything into ./dist/. Does NOT wipe existing dist/ contents, so it can be
    run alongside build-dist.ps1. Use build.ps1 to run both together with a clean
    slate and a test gate.

.PARAMETER Version
    Version string embedded in zip names. Reads ./VERSION if omitted.

.EXAMPLE
    .\build-source.ps1
    .\build-source.ps1 -Version 1.2.0
#>
param(
    [string]$Version = ""
)

$ErrorActionPreference = "Stop"
$Root                  = $PSScriptRoot
$Dist                  = Join-Path $Root "dist"
$Staging               = Join-Path $Dist "_src-staging"

if (-not $Version) {
    $versionFile = Join-Path $Root "VERSION"
    if (-not (Test-Path $versionFile)) { throw "VERSION file not found at $versionFile" }
    $Version = (Get-Content $versionFile -Raw).Trim()
}

# Directories to exclude when copying source trees
$ExcludeDirs = @("bin", "obj", ".vs", ".git", ".idea", ".vscode", "dist", "_staging", "_src-staging", "_bin-staging")

# ── Helpers ───────────────────────────────────────────────────────────────────

function Copy-ProjectSource {
    param(
        [string]$ProjectDir,
        [string]$DestRoot
    )

    $src  = Join-Path $Root $ProjectDir
    $dest = Join-Path $DestRoot $ProjectDir

    if (-not (Test-Path $src)) { throw "Project directory not found: $src" }

    Get-ChildItem $src -Recurse -Force |
        Where-Object {
            $rel      = $_.FullName.Substring($src.Length).TrimStart('\/')
            $segments = $rel -split '[/\\]'
            -not ($segments | Where-Object { $ExcludeDirs -contains $_ })
        } |
        ForEach-Object {
            $relPath = $_.FullName.Substring($src.Length).TrimStart('\/')
            $target  = Join-Path $dest $relPath
            if ($_.PSIsContainer) {
                New-Item $target -ItemType Directory -Force | Out-Null
            } else {
                $targetDir = Split-Path $target -Parent
                if (-not (Test-Path $targetDir)) {
                    New-Item $targetDir -ItemType Directory -Force | Out-Null
                }
                Copy-Item $_.FullName -Destination $target -Force
            }
        }
}

function Write-SolutionFile {
    param(
        [string]   $DestRoot,
        [string]   $SolutionName,
        [string[]] $Projects
    )

    $lines = @('<Solution>')
    foreach ($p in $Projects) {
        $lines += "  <Project Path=`"$p/$p.csproj`" />"
    }
    $lines += '</Solution>'

    $slnxPath = Join-Path $DestRoot "$SolutionName.slnx"
    $lines -join [Environment]::NewLine | Set-Content $slnxPath -Encoding UTF8
}

function New-SourcePackage {
    param(
        [string]   $ZipBaseName,
        [string]   $StagingSubDir,
        [string]   $SolutionName,
        [string[]] $Projects
    )

    $stageDir = Join-Path $Staging $StagingSubDir
    New-Item $stageDir -ItemType Directory -Force | Out-Null

    Write-Host ""
    Write-Host "Packaging $ZipBaseName source..." -ForegroundColor Cyan

    foreach ($project in $Projects) {
        Write-Host "  collecting $project"
        Copy-ProjectSource -ProjectDir $project -DestRoot $stageDir
    }

    # Root files recipients need to build and orient themselves
    foreach ($file in @("README.md", "VERSION")) {
        $src = Join-Path $Root $file
        if (Test-Path $src) { Copy-Item $src -Destination $stageDir -Force }
    }

    Write-SolutionFile -DestRoot $stageDir -SolutionName $SolutionName -Projects $Projects

    $zipPath = Join-Path $Dist "$ZipBaseName-$Version-src.zip"
    Compress-Archive -Path "$stageDir\*" -DestinationPath $zipPath -Force
    Write-Host "  -> $(Split-Path $zipPath -Leaf)" -ForegroundColor Green
}

# ── Clean only our own staging area ───────────────────────────────────────────

if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item $Dist    -ItemType Directory -Force | Out-Null
New-Item $Staging -ItemType Directory        | Out-Null

# ── Package each app ──────────────────────────────────────────────────────────

# DialogEditor.Tests references Core, Patch, and ViewModels.
# ViewModels must therefore appear in every package that includes Tests.

New-SourcePackage `
    -ZipBaseName   "PillarsDialogEditor" `
    -StagingSubDir "editor-src" `
    -SolutionName  "PillarsDialogEditor" `
    -Projects      @(
        "DialogEditor.Core",
        "DialogEditor.Patch",
        "DialogEditor.ViewModels",
        "DialogEditor.Avalonia.Shared",
        "DialogEditor.Avalonia",
        "DialogEditor.Tests"
    )

New-SourcePackage `
    -ZipBaseName   "PatchManager" `
    -StagingSubDir "patchmanager-src" `
    -SolutionName  "PatchManager" `
    -Projects      @(
        "DialogEditor.Core",
        "DialogEditor.Patch",
        "DialogEditor.ViewModels",
        "DialogEditor.Avalonia.Shared",
        "DialogEditor.PatchManager",
        "DialogEditor.Tests"
    )

New-SourcePackage `
    -ZipBaseName   "dialog-patcher" `
    -StagingSubDir "cli-src" `
    -SolutionName  "dialog-patcher" `
    -Projects      @(
        "DialogEditor.Core",
        "DialogEditor.Patch",
        "DialogEditor.ViewModels",
        "DialogEditor.PatchCli",
        "DialogEditor.Tests"
    )

# ── Tidy ──────────────────────────────────────────────────────────────────────

Remove-Item $Staging -Recurse -Force

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "Source archives written to: $Dist" -ForegroundColor Green
Get-ChildItem $Dist -Filter "*-src.zip" | Sort-Object Name | ForEach-Object {
    $sizeKB = [math]::Round($_.Length / 1KB, 0)
    Write-Host ("  {0,-50} {1,6} KB" -f $_.Name, $sizeKB)
}
