<#
.SYNOPSIS
    Prove every source archive from build-source.ps1 builds on its own.

.DESCRIPTION
    Runs build-source.ps1, extracts each dist/*-src.zip into a fresh folder
    OUTSIDE the repository, and runs `dotnet build` on the .slnx inside it.
    Exits non-zero if any archive fails to build.

    Extracting outside the repo is the whole point: MSBuild walks up the
    directory tree for Directory.Packages.props / Directory.Build.props, so an
    archive extracted under the checkout would silently borrow the repo's
    root files and hide exactly the "file missing from the zip" class of bug
    this check exists to catch.

    Runs locally or in CI; under GitHub Actions a failure is also emitted as
    an ::error annotation.

.PARAMETER Version
    Version string passed to build-source.ps1. Only affects zip names.

.EXAMPLE
    ./tools/ci/Test-SourceArchives.ps1
#>
param(
    [string]$Version = "0.0.0-srccheck"
)

$ErrorActionPreference = "Stop"
$Root = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$Dist = Join-Path $Root "dist"

& (Join-Path $Root "build-source.ps1") -Version $Version

$zips = @(Get-ChildItem $Dist -Filter "*-$Version-src.zip")
if ($zips.Count -eq 0) { throw "build-source.ps1 produced no *-$Version-src.zip in $Dist" }

$tempBase = if ($env:RUNNER_TEMP) { $env:RUNNER_TEMP } else { [IO.Path]::GetTempPath() }
$workRoot = Join-Path $tempBase ("srccheck-" + [guid]::NewGuid().ToString("N").Substring(0, 8))
New-Item $workRoot -ItemType Directory | Out-Null

$failed = @()
try {
    foreach ($zip in $zips) {
        $name    = $zip.Name -replace "-$([regex]::Escape($Version))-src\.zip$", ""
        $extract = Join-Path $workRoot $name
        Expand-Archive $zip.FullName -DestinationPath $extract

        $slnx = @(Get-ChildItem $extract -Filter "*.slnx")
        if ($slnx.Count -ne 1) {
            Write-Host "[$name] expected exactly one .slnx at the archive root, found $($slnx.Count)" -ForegroundColor Red
            $failed += $name
            continue
        }

        Write-Host ""
        Write-Host "[$name] dotnet build $($slnx[0].Name)" -ForegroundColor Cyan
        & dotnet build $slnx[0].FullName -c Release --nologo -v quiet "-clp:ErrorsOnly"
        if ($LASTEXITCODE -ne 0) {
            $failed += $name
            if ($env:GITHUB_ACTIONS) {
                Write-Host "::error::Source archive $($zip.Name) does not build standalone (see the build output above)."
            }
        } else {
            Write-Host "[$name] OK" -ForegroundColor Green
        }
    }
} finally {
    Remove-Item $workRoot -Recurse -Force -ErrorAction SilentlyContinue
    $zips | Remove-Item -Force -ErrorAction SilentlyContinue
}

Write-Host ""
if ($failed.Count -gt 0) {
    Write-Host "Source archives that do not build standalone: $($failed -join ', ')" -ForegroundColor Red
    exit 1
}
Write-Host "All $($zips.Count) source archives build standalone." -ForegroundColor Green
