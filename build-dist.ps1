<#
.SYNOPSIS
    Publish the two distributable products as self-contained win-x64 binaries.

.DESCRIPTION
    Produces one zip per product under ./dist/:

      PillarsDialogEditor-<VERSION>.zip          the editor
      PillarsDialogPatcher-<PATCHER_VERSION>.zip the mod installer for players:
          DialogEditor.PatchManager.exe  (GUI, at the top so it's the obvious one to run)
          README.md                      (player-facing)
          cli\dialog-patcher.exe         (command line, for scripts and installers)

    The two are versioned and released independently (issue #77): a player who only
    wants to install a mod never has to download the editor, and an editor-only fix
    doesn't produce a "new" patcher.

    Does NOT wipe existing dist/ contents, so it can be run alongside
    build-source.ps1. Use build.ps1 to run both together with a clean slate and a
    test gate.

.PARAMETER Product
    Editor, Patcher, or All (default).

.PARAMETER Version
    Editor version. Reads ./VERSION if omitted.

.PARAMETER PatcherVersion
    Patcher version. Reads ./PATCHER_VERSION if omitted.

.PARAMETER Configuration
    Build configuration. Defaults to "Release".

.EXAMPLE
    .\build-dist.ps1
    .\build-dist.ps1 -Product Patcher -PatcherVersion 1.0.1
#>
param(
    [ValidateSet("All", "Editor", "Patcher")]
    [string]$Product        = "All",
    [string]$Version        = "",
    [string]$PatcherVersion = "",
    [string]$Configuration  = "Release"
)

$ErrorActionPreference = "Stop"
$Runtime               = "win-x64"
$Root                  = $PSScriptRoot
$Dist                  = Join-Path $Root "dist"
$Staging               = Join-Path $Dist "_bin-staging"

. (Join-Path $Root "tools\build\Read-VersionFile.ps1")

$buildEditor  = $Product -in "All", "Editor"
$buildPatcher = $Product -in "All", "Patcher"

if ($buildEditor  -and -not $Version)        { $Version        = Read-VersionFile -Root $Root -Product Editor }
if ($buildPatcher -and -not $PatcherVersion) { $PatcherVersion = Read-VersionFile -Root $Root -Product Patcher }

# ── Clean only our own staging area ───────────────────────────────────────────

if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item $Dist    -ItemType Directory -Force | Out-Null
New-Item $Staging -ItemType Directory        | Out-Null

# ── Helpers ───────────────────────────────────────────────────────────────────

function Publish-Project {
    param(
        [string]$ProjectPath,
        [string]$OutDir,
        [string]$ProjectVersion,
        [switch]$SingleFile
    )

    Write-Host ""
    Write-Host "Publishing $(Split-Path $ProjectPath -Leaf) $ProjectVersion..." -ForegroundColor Cyan

    $publishArgs = @(
        "publish", (Join-Path $Root $ProjectPath)
        "-c", $Configuration
        "-r", $Runtime
        "--self-contained", "true"
        "-o", $OutDir
        "/p:Version=$ProjectVersion"
        "/p:DebugType=None"
        "/p:DebugSymbols=false"
    )

    if ($SingleFile) {
        $publishArgs += "/p:PublishSingleFile=true"
        $publishArgs += "/p:EnableCompressionInSingleFile=true"
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $ProjectPath" }

    Get-ChildItem $OutDir -Include "*.pdb", "*.xml" -Recurse | Remove-Item -Force
}

function Compress-Staged {
    param([string]$StageDir, [string]$ZipName)

    $zipPath = Join-Path $Dist $ZipName
    Compress-Archive -Path "$StageDir\*" -DestinationPath $zipPath -Force
    Write-Host "  -> $ZipName" -ForegroundColor Green
}

# ── Editor ────────────────────────────────────────────────────────────────────

if ($buildEditor) {
    $editorStage = Join-Path $Staging "editor"
    Publish-Project -ProjectPath "DialogEditor.Avalonia\DialogEditor.Avalonia.csproj" `
                    -OutDir $editorStage -ProjectVersion $Version
    Compress-Staged -StageDir $editorStage -ZipName "PillarsDialogEditor-$Version.zip"
}

# ── Pillars Dialog Patcher ────────────────────────────────────────────────────

if ($buildPatcher) {
    $patcherStage = Join-Path $Staging "patcher"

    Publish-Project -ProjectPath "DialogEditor.PatchManager\DialogEditor.PatchManager.csproj" `
                    -OutDir $patcherStage -ProjectVersion $PatcherVersion

    # The self-contained runtime drops createdump.exe (debugger crash dumps) beside the
    # app. Nothing here uses it, and a second top-level .exe is exactly the "which one
    # do I run?" confusion the layout below exists to avoid.
    Remove-Item (Join-Path $patcherStage "createdump.exe") -Force -ErrorAction SilentlyContinue

    # cli\ keeps the console exe out of the top level, so the only .exe a player sees
    # there is the GUI. Double-clicking it anyway gets a note pointing back up to the
    # Patch Manager (SystemConsoleLaunch in DialogEditor.PatchCli).
    Publish-Project -ProjectPath "DialogEditor.PatchCli\DialogEditor.PatchCli.csproj" `
                    -OutDir (Join-Path $patcherStage "cli") -ProjectVersion $PatcherVersion -SingleFile

    Copy-Item (Join-Path $Root "docs\patcher\README.md") -Destination $patcherStage -Force

    Compress-Staged -StageDir $patcherStage -ZipName "PillarsDialogPatcher-$PatcherVersion.zip"
}

# ── Tidy ──────────────────────────────────────────────────────────────────────

Remove-Item $Staging -Recurse -Force

# ── Summary ───────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "Binary archives written to: $Dist" -ForegroundColor Green
Get-ChildItem $Dist -Filter "*.zip" | Where-Object { $_.Name -notlike "*-src.zip" } |
    Sort-Object Name | ForEach-Object {
        $sizeMB = [math]::Round($_.Length / 1MB, 1)
        Write-Host ("  {0,-45} {1,6} MB" -f $_.Name, $sizeMB)
    }
