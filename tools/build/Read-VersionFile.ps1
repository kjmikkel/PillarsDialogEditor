<#
.SYNOPSIS
    Read a product's version file (VERSION or PATCHER_VERSION) from the repo root.

.DESCRIPTION
    The editor and the Pillars Dialog Patcher are versioned independently (issue #77):
    VERSION drives the editor, PATCHER_VERSION drives the Patch Manager GUI and
    dialog-patcher CLI. Dot-source this from the build scripts so they all resolve a
    product's version the same way.

.EXAMPLE
    . (Join-Path $PSScriptRoot "tools\build\Read-VersionFile.ps1")
    $v = Read-VersionFile -Root $PSScriptRoot -Product Patcher
#>

function Read-VersionFile {
    param(
        [Parameter(Mandatory)][string]$Root,
        [Parameter(Mandatory)][ValidateSet("Editor", "Patcher")][string]$Product
    )

    $name = if ($Product -eq "Patcher") { "PATCHER_VERSION" } else { "VERSION" }
    $path = Join-Path $Root $name
    if (-not (Test-Path $path)) { throw "$name file not found at $path" }
    $version = (Get-Content $path -Raw).Trim()
    if (-not $version) { throw "$name at $path is empty" }
    return $version
}
