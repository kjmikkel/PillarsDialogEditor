<#
.SYNOPSIS
    Publish the three distributable applications as self-contained win-x64 binaries.

.DESCRIPTION
    Produces one zip per app under ./dist/. Does NOT wipe existing dist/ contents,
    so it can be run alongside build-source.ps1. Use build.ps1 to run both together
    with a clean slate and a test gate.

.PARAMETER Version
    Version string embedded in zip names and the CLI binary. Reads ./VERSION if omitted.

.PARAMETER Configuration
    Build configuration. Defaults to "Release".

.EXAMPLE
    .\build-dist.ps1
    .\build-dist.ps1 -Version 1.2.0
#>
param(
    [string]$Version       = "",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Runtime               = "win-x64"
$Root                  = $PSScriptRoot
$Dist                  = Join-Path $Root "dist"
$Staging               = Join-Path $Dist "_bin-staging"

if (-not $Version) {
    $versionFile = Join-Path $Root "VERSION"
    if (-not (Test-Path $versionFile)) { throw "VERSION file not found at $versionFile" }
    $Version = (Get-Content $versionFile -Raw).Trim()
}

# ── Clean only our own staging area ───────────────────────────────────────────

if (Test-Path $Staging) { Remove-Item $Staging -Recurse -Force }
New-Item $Dist    -ItemType Directory -Force | Out-Null
New-Item $Staging -ItemType Directory        | Out-Null

# ── Publish helper ────────────────────────────────────────────────────────────

function Publish-And-Zip {
    param(
        [string]$ProjectPath,
        [string]$ZipBaseName,
        [string]$StagingSubDir,
        [switch]$SingleFile
    )

    $projectFull = Join-Path $Root $ProjectPath
    $outDir      = Join-Path $Staging $StagingSubDir
    $zipPath     = Join-Path $Dist "$ZipBaseName-$Version.zip"

    Write-Host ""
    Write-Host "Publishing $ZipBaseName..." -ForegroundColor Cyan

    $publishArgs = @(
        "publish", $projectFull
        "-c", $Configuration
        "-r", $Runtime
        "--self-contained", "true"
        "-o", $outDir
        "/p:Version=$Version"
        "/p:DebugType=None"
        "/p:DebugSymbols=false"
    )

    if ($SingleFile) {
        $publishArgs += "/p:PublishSingleFile=true"
        $publishArgs += "/p:EnableCompressionInSingleFile=true"
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $ZipBaseName" }

    Get-ChildItem $outDir -Include "*.pdb", "*.xml" -Recurse | Remove-Item -Force

    Compress-Archive -Path "$outDir\*" -DestinationPath $zipPath -Force
    Write-Host "  -> $(Split-Path $zipPath -Leaf)" -ForegroundColor Green
}

# ── Build each app ────────────────────────────────────────────────────────────

Publish-And-Zip `
    -ProjectPath   "DialogEditor.Avalonia\DialogEditor.Avalonia.csproj" `
    -ZipBaseName   "PillarsDialogEditor" `
    -StagingSubDir "editor"

Publish-And-Zip `
    -ProjectPath   "DialogEditor.PatchManager\DialogEditor.PatchManager.csproj" `
    -ZipBaseName   "PatchManager" `
    -StagingSubDir "patchmanager"

Publish-And-Zip `
    -ProjectPath   "DialogEditor.PatchCli\DialogEditor.PatchCli.csproj" `
    -ZipBaseName   "dialog-patcher" `
    -StagingSubDir "cli" `
    -SingleFile

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
