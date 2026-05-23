<#
.SYNOPSIS
    Run tests, then produce all binary and source distribution archives.

.DESCRIPTION
    Single entry point for a release build:
      1. Reads the version from ./VERSION (or -Version parameter)
      2. Runs the full test suite — aborts on failure unless -SkipTests
      3. Wipes and recreates ./dist/
      4. Calls build-dist.ps1  — three self-contained win-x64 binary zips
      5. Calls build-source.ps1 — three source-code zips
      6. Prints a final summary of all six archives

.PARAMETER Version
    Version string to embed in file names and the CLI binary.
    Reads ./VERSION if omitted.

.PARAMETER SkipTests
    Skip the test gate. Use only when you know the tests pass.

.PARAMETER Configuration
    Build configuration passed to build-dist.ps1. Defaults to "Release".

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Version 1.2.0
    .\build.ps1 -SkipTests
#>
param(
    [string]$Version       = "",
    [switch]$SkipTests,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Dist = Join-Path $Root "dist"

# ── Resolve version ───────────────────────────────────────────────────────────

if (-not $Version) {
    $versionFile = Join-Path $Root "VERSION"
    if (-not (Test-Path $versionFile)) { throw "VERSION file not found at $versionFile" }
    $Version = (Get-Content $versionFile -Raw).Trim()
}

Write-Host "=== Pillars Dialog Editor — release build v$Version ===" -ForegroundColor White

# ── Test gate ─────────────────────────────────────────────────────────────────

if ($SkipTests) {
    Write-Host ""
    Write-Host "Tests skipped (-SkipTests)." -ForegroundColor DarkYellow
} else {
    Write-Host ""
    Write-Host "Running tests..." -ForegroundColor Cyan
    & dotnet test (Join-Path $Root "DialogEditor.Tests\DialogEditor.Tests.csproj") `
        -c $Configuration --verbosity quiet
    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Tests failed. Aborting release build." -ForegroundColor Red
        exit 1
    }
    Write-Host "All tests passed." -ForegroundColor Green
}

# ── Clean dist/ ───────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "Cleaning dist/..." -ForegroundColor DarkGray
if (Test-Path $Dist) { Remove-Item $Dist -Recurse -Force }
New-Item $Dist -ItemType Directory | Out-Null

# ── Binary archives ───────────────────────────────────────────────────────────

& (Join-Path $Root "build-dist.ps1") -Version $Version -Configuration $Configuration

# ── Source archives ───────────────────────────────────────────────────────────

& (Join-Path $Root "build-source.ps1") -Version $Version

# ── Final summary ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Release build complete — v$Version ===" -ForegroundColor Green
Write-Host ""
Write-Host "Binary archives:" -ForegroundColor White
Get-ChildItem $Dist -Filter "*.zip" | Where-Object { $_.Name -notlike "*-src.zip" } |
    Sort-Object Name | ForEach-Object {
        $sizeMB = [math]::Round($_.Length / 1MB, 1)
        Write-Host ("  {0,-45} {1,6} MB" -f $_.Name, $sizeMB)
    }
Write-Host ""
Write-Host "Source archives:" -ForegroundColor White
Get-ChildItem $Dist -Filter "*-src.zip" | Sort-Object Name | ForEach-Object {
    $sizeKB = [math]::Round($_.Length / 1KB, 0)
    Write-Host ("  {0,-50} {1,6} KB" -f $_.Name, $sizeKB)
}
