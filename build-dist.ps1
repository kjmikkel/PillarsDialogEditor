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
          runtime\                       (one .NET runtime both exes use)

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
        [switch]$SingleFile,
        # When set, publish framework-dependent and make the exe look for .NET ONLY at
        # this path relative to itself (a dotnet-root layout; see Copy-SharedRuntime).
        # Never a global install, so the player's machine can't change what runs.
        [string]$RelativeDotNet = ""
    )

    Write-Host ""
    Write-Host "Publishing $(Split-Path $ProjectPath -Leaf) $ProjectVersion..." -ForegroundColor Cyan

    $selfContained = if ($RelativeDotNet) { "false" } else { "true" }
    $publishArgs = @(
        "publish", (Join-Path $Root $ProjectPath)
        "-c", $Configuration
        "-r", $Runtime
        "--self-contained", $selfContained
        "-o", $OutDir
        "/p:Version=$ProjectVersion"
        "/p:DebugType=None"
        "/p:DebugSymbols=false"
    )

    if ($RelativeDotNet) {
        $publishArgs += "/p:AppHostDotNetSearch=AppRelative"
        $publishArgs += "/p:AppHostRelativeDotNet=$RelativeDotNet"
    }

    if ($SingleFile) {
        $publishArgs += "/p:PublishSingleFile=true"
        # The SDK only compresses self-contained bundles (NETSDK1176); without a runtime
        # inside, a framework-dependent bundle is small anyway.
        if (-not $RelativeDotNet) { $publishArgs += "/p:EnableCompressionInSingleFile=true" }
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $ProjectPath" }

    Get-ChildItem $OutDir -Include "*.pdb", "*.xml" -Recurse | Remove-Item -Force
}

function Copy-SharedRuntime {
    # Copies one .NET runtime, in dotnet-root layout, from the build machine's own .NET
    # install into $DestDir — the same runtime a self-contained publish would have
    # bundled, but once, for every app in the zip:
    #   host\fxr\<ver>\hostfxr.dll                (the resolver; newest, it's backward-compatible)
    #   shared\Microsoft.NETCore.App\<ver>\...    (newest patch of the version the app targets)
    param([string]$RuntimeConfig, [string]$DestDir)

    $target = (Get-Content $RuntimeConfig -Raw | ConvertFrom-Json).runtimeOptions.framework
    if ($target.name -ne "Microsoft.NETCore.App") { throw "Unexpected framework '$($target.name)' in $RuntimeConfig" }
    $band = ([version]$target.version).ToString(2)   # e.g. "10.0"

    $installed = & dotnet --list-runtimes | ForEach-Object {
        if ($_ -match '^Microsoft\.NETCore\.App (\S+) \[(.+)\]$') {
            [pscustomobject]@{ Version = $Matches[1]; SharedDir = $Matches[2] }
        }
    } | Where-Object { $_.Version -notmatch '-' -and ([version]$_.Version).ToString(2) -eq $band } |
        Sort-Object { [version]$_.Version } -Descending | Select-Object -First 1
    if (-not $installed) { throw "No Microsoft.NETCore.App $band.x runtime installed to bundle (dotnet --list-runtimes)." }

    $dotnetRoot = Split-Path (Split-Path $installed.SharedDir -Parent) -Parent
    $fxr = Get-ChildItem (Join-Path $dotnetRoot "host\fxr") -Directory |
        Where-Object { $_.Name -notmatch '-' } |
        Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
    if (-not $fxr) { throw "No host\fxr found under $dotnetRoot" }

    Write-Host ""
    Write-Host "Bundling .NET runtime $($installed.Version) (hostfxr $($fxr.Name)) from $dotnetRoot..." -ForegroundColor Cyan

    $sharedDest = Join-Path $DestDir "shared\Microsoft.NETCore.App"
    $fxrDest    = Join-Path $DestDir "host\fxr"
    New-Item $sharedDest, $fxrDest -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $installed.SharedDir $installed.Version) -Destination $sharedDest -Recurse
    Copy-Item $fxr.FullName -Destination $fxrDest -Recurse
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

    # Both apps share ONE runtime in runtime\ instead of each bundling its own
    # (self-contained): roughly 30 MB less to download, and no createdump.exe beside
    # the GUI. Each exe is pointed at runtime\ relative to itself.
    Publish-Project -ProjectPath "DialogEditor.PatchManager\DialogEditor.PatchManager.csproj" `
                    -OutDir $patcherStage -ProjectVersion $PatcherVersion -RelativeDotNet "runtime"

    # cli\ keeps the console exe out of the top level, so the only .exe a player sees
    # there is the GUI. Double-clicking it anyway gets a note pointing back up to the
    # Patch Manager (SystemConsoleLaunch in DialogEditor.PatchCli).
    $cliStage = Join-Path $patcherStage "cli"
    Publish-Project -ProjectPath "DialogEditor.PatchCli\DialogEditor.PatchCli.csproj" `
                    -OutDir $cliStage -ProjectVersion $PatcherVersion -SingleFile -RelativeDotNet "..\runtime"

    Copy-SharedRuntime -RuntimeConfig (Join-Path $patcherStage "DialogEditor.PatchManager.runtimeconfig.json") `
                       -DestDir (Join-Path $patcherStage "runtime")

    Copy-Item (Join-Path $Root "docs\patcher\README.md") -Destination $patcherStage -Force

    # Smoke test from the staged layout: the CLI only starts if it finds runtime\ where
    # its exe was told to look, and never falls back to a system .NET — so a zip that
    # would say "You must install .NET" on a player's machine fails here instead.
    $smoke = & (Join-Path $cliStage "dialog-patcher.exe") --version 2>&1
    if ($LASTEXITCODE -ne 0 -or -not ($smoke -match [regex]::Escape($PatcherVersion))) {
        throw "Staged dialog-patcher did not start against the bundled runtime:`n$($smoke -join "`n")"
    }
    Write-Host "  staged dialog-patcher starts on the bundled runtime: $($smoke[0])" -ForegroundColor DarkGray

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
