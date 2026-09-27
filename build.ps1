<#
.SYNOPSIS
    Run tests, then produce the binary and source distribution archives.

.DESCRIPTION
    Single entry point for a release build of one or both products — the editor
    (./VERSION, tags v*) and the Pillars Dialog Patcher (./PATCHER_VERSION, tags
    patcher-v*), which are versioned and released independently (issue #77):
      1. Resolves each selected product's version (file, or -Version / -PatcherVersion)
      2. Runs the full test suite — aborts on failure unless -SkipTests
      3. Wipes and recreates ./dist/
      4. Calls build-dist.ps1   — one self-contained win-x64 binary zip per product
      5. Calls build-source.ps1 — one source-code zip per product
      6. Writes dist/SHA256SUMS.txt (sha256sum format) covering every zip
      7. Prints a final summary

.PARAMETER Product
    Editor, Patcher, or All (default).

.PARAMETER Version
    Editor version. Reads ./VERSION if omitted.

.PARAMETER PatcherVersion
    Patcher version. Reads ./PATCHER_VERSION if omitted.

.PARAMETER SkipTests
    Skip the test gate. Use only when you know the tests pass.

.PARAMETER Configuration
    Build configuration passed to build-dist.ps1. Defaults to "Release".

.EXAMPLE
    .\build.ps1
    .\build.ps1 -Product Patcher
    .\build.ps1 -Version 1.2.0 -SkipTests
#>
param(
    [ValidateSet("All", "Editor", "Patcher")]
    [string]$Product        = "All",
    [string]$Version        = "",
    [string]$PatcherVersion = "",
    [switch]$SkipTests,
    [string]$Configuration  = "Release"
)

$ErrorActionPreference = "Stop"
$Root = $PSScriptRoot
$Dist = Join-Path $Root "dist"

# ── Resolve versions ──────────────────────────────────────────────────────────

. (Join-Path $Root "tools\build\Read-VersionFile.ps1")

$buildEditor  = $Product -in "All", "Editor"
$buildPatcher = $Product -in "All", "Patcher"

if ($buildEditor  -and -not $Version)        { $Version        = Read-VersionFile -Root $Root -Product Editor }
if ($buildPatcher -and -not $PatcherVersion) { $PatcherVersion = Read-VersionFile -Root $Root -Product Patcher }

$Title = @(
    if ($buildEditor)  { "Pillars Dialog Editor v$Version" }
    if ($buildPatcher) { "Pillars Dialog Patcher v$PatcherVersion" }
) -join " + "

Write-Host "=== Release build: $Title ===" -ForegroundColor White

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

& (Join-Path $Root "build-dist.ps1") -Product $Product -Version $Version -PatcherVersion $PatcherVersion `
    -Configuration $Configuration

# ── Source archives ───────────────────────────────────────────────────────────

& (Join-Path $Root "build-source.ps1") -Product $Product -Version $Version -PatcherVersion $PatcherVersion

# ── Checksums ─────────────────────────────────────────────────────────────────
# sha256sum's own format ("<hash>  <file>"), so `sha256sum -c SHA256SUMS.txt` verifies
# a download in Git Bash or on Linux; on Windows, Get-FileHash prints the same hash.

$sums = Get-ChildItem $Dist -Filter "*.zip" | Sort-Object Name | ForEach-Object {
    "{0}  {1}" -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllText((Join-Path $Dist "SHA256SUMS.txt"), (($sums -join "`n") + "`n"))

# ── Final summary ─────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "=== Release build complete: $Title ===" -ForegroundColor Green
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
Write-Host ""
Write-Host "Checksums: dist/SHA256SUMS.txt" -ForegroundColor White
