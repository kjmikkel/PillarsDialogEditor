<#
.SYNOPSIS
    CI gate: fail the build on NuGet packages with known vulnerabilities.

.DESCRIPTION
    Runs `dotnet list package --vulnerable --include-transitive` over the solution,
    reports EVERY finding as a GitHub annotation (so nothing is hidden), and fails
    only for the findings Test-FindingBlocksBuild says should block.

    Why a policy function rather than "fail on anything": as of 2026-09 the test
    projects carry System.Net.Http 4.3.0 and System.Text.RegularExpressions 4.3.0
    (High), pulled in by xunit 2.x -> NETStandard.Library 1.6.1. They never ship,
    on net8.0 the framework's own assemblies win over those package versions, and
    Avalonia.Headless.XUnit pins us to xunit v2 so an upgrade can't remove them.
    Shipping projects (editor, PatchManager, dialog-patcher and their libraries)
    are clean. Where the line sits between those two cases is a policy decision.

.EXAMPLE
    ./tools/ci/Assert-NoVulnerablePackages.ps1
#>
$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..\..")

# ── Policy ────────────────────────────────────────────────────────────────────

function Test-FindingBlocksBuild {
    <#
    Decide whether one vulnerable-package finding should fail CI.

    $Finding has:
      .Project        project name, e.g. "DialogEditor.Core" or "DialogEditor.Tests"
      .IsTestProject  $true for *.Tests projects (never shipped to users)
      .Package        package id, e.g. "System.Net.Http"
      .Version        resolved version, e.g. "4.3.0"
      .Transitive     $true if pulled in indirectly, $false if a direct PackageReference
      .Severity       "Low" | "Moderate" | "High" | "Critical"
      .Advisory       advisory URL

    Return $true to fail the build, $false to report it as a warning only.
    #>
    param([Parameter(Mandatory)] $Finding)

    # Policy: strict where users are exposed, lenient where only CI is — but not
    # blind, because test dependencies still EXECUTE on the runner, including the
    # release job whose token can create releases. A Critical there (e.g. a
    # compromised package) is a pipeline risk worth stopping for.
    $rank = @{ Low = 1; Moderate = 2; High = 3; Critical = 4 }[$Finding.Severity]
    if ($null -eq $rank) { return $true }   # unknown severity label: fail safe, go look

    if ($Finding.IsTestProject) {
        # Never shipped, but runs on CI.
        return $rank -ge 4
    }
    # Shipped to users: direct or transitive makes no difference to exposure.
    return $rank -ge 2
}

# ── Collect findings ──────────────────────────────────────────────────────────

$json = & dotnet list (Join-Path $Root "DialogEditor.slnx") package `
    --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) { throw "dotnet list package failed (exit $LASTEXITCODE)" }

$report   = ($json -join "`n") | ConvertFrom-Json
$findings = foreach ($project in $report.projects) {
    $name = [IO.Path]::GetFileNameWithoutExtension($project.path)
    foreach ($fw in @($project.frameworks)) {
        $packages = @(
            @($fw.topLevelPackages)   | Where-Object { $_ } | ForEach-Object { $_ | Add-Member -PassThru -NotePropertyName Transitive -NotePropertyValue $false -Force }
            @($fw.transitivePackages) | Where-Object { $_ } | ForEach-Object { $_ | Add-Member -PassThru -NotePropertyName Transitive -NotePropertyValue $true -Force }
        )
        foreach ($pkg in $packages) {
            foreach ($vuln in @($pkg.vulnerabilities)) {
                [pscustomobject]@{
                    Project       = $name
                    IsTestProject = $name -like "*.Tests"
                    Package       = $pkg.id
                    Version       = $pkg.resolvedVersion
                    Transitive    = $pkg.Transitive
                    Severity      = $vuln.severity
                    Advisory      = $vuln.advisoryurl
                }
            }
        }
    }
}
# A package can appear once per target framework; report it once per project.
$findings = @($findings | Sort-Object Project, Package, Version, Advisory -Unique)

# ── Report and decide ─────────────────────────────────────────────────────────

$blocking = 0
foreach ($f in $findings) {
    $kind = if ($f.Transitive) { "transitive" } else { "direct" }
    $msg  = "$($f.Project): $($f.Package) $($f.Version) ($kind) — $($f.Severity) — $($f.Advisory)"
    if (Test-FindingBlocksBuild $f) {
        Write-Host "::error title=Vulnerable package::$msg"
        $blocking++
    } else {
        Write-Host "::warning title=Vulnerable package (not blocking)::$msg"
    }
}

Write-Host "$($findings.Count) vulnerable package finding(s), $blocking blocking."
if ($blocking -gt 0) { exit 1 }
