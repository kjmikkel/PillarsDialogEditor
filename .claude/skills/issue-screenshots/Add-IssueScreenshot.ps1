<#
.SYNOPSIS
  Upload screenshots to a GitHub repo and attach them to an issue, using only `gh`.

.DESCRIPTION
  GitHub's drag-and-drop image upload (user-attachments) has no public API, so
  this stores images on a dedicated orphan branch (default `issue-assets`) via the
  Contents API and links them with github.com/<repo>/raw/<branch>/<path> URLs.
  Those URLs follow the repo's visibility: anyone who can see the issue can see
  the image, and nobody else can.

  Self-contained and repo-agnostic: the repo is detected from the current
  directory, and the assets branch is created on first use without touching
  the local checkout.

.EXAMPLE
  ./Add-IssueScreenshot.ps1 -Issue 42                  # newest file in Pictures\Screenshots
  ./Add-IssueScreenshot.ps1 -Issue 42 -Clipboard       # image currently on the clipboard
  ./Add-IssueScreenshot.ps1 -Issue 42 -Path a.png,b.png -Caption 'Before / after'
  ./Add-IssueScreenshot.ps1 -Path a.png                # no issue: upload, print markdown only
#>
[CmdletBinding()]
param(
    # Issue (or PR) number to comment on. Omit to upload and print markdown only,
    # e.g. to embed in the body of an issue that does not exist yet.
    [int]$Issue,
    [string[]]$Path,
    [switch]$Clipboard,
    [string]$Caption,
    # Upload and print the markdown, but do not post a comment.
    [switch]$NoComment,
    [string]$Repo,
    [string]$Branch = 'issue-assets'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-GhJson {
    param([string]$Method, [string]$Endpoint, [hashtable]$Body)
    # Pass the body through a file: base64 images blow past Windows' ~32K
    # command-line limit if passed with -f.
    $tmp = New-TemporaryFile
    try {
        $Body | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $tmp -Encoding utf8NoBOM
        $out = gh api -X $Method $Endpoint --input $tmp.FullName
        if ($LASTEXITCODE -ne 0) { throw "gh api $Method $Endpoint failed" }
        $out | ConvertFrom-Json
    } finally { Remove-Item -LiteralPath $tmp -ErrorAction SilentlyContinue }
}

function Initialize-AssetsBranch {
    param([string]$Repo, [string]$Branch)
    gh api "repos/$Repo/git/ref/heads/$Branch" --silent 2>$null
    if ($LASTEXITCODE -eq 0) { return }

    # Orphan branch, built entirely server-side: tree -> parentless commit -> ref.
    Write-Host "Creating orphan branch '$Branch' on $Repo..."
    $readme = "# Issue assets`n`nScreenshots referenced from issues. Not part of the source tree; safe to prune.`n"
    $tree = Invoke-GhJson POST "repos/$Repo/git/trees" @{
        tree = @(@{ path = 'README.md'; mode = '100644'; type = 'blob'; content = $readme })
    }
    $commit = Invoke-GhJson POST "repos/$Repo/git/commits" @{
        message = 'Initialize issue-assets branch'; tree = $tree.sha; parents = @()
    }
    Invoke-GhJson POST "repos/$Repo/git/refs" @{ ref = "refs/heads/$Branch"; sha = $commit.sha } | Out-Null
}

function Get-ClipboardPng {
    Add-Type -AssemblyName System.Windows.Forms, System.Drawing
    $img = [System.Windows.Forms.Clipboard]::GetImage()
    if (-not $img) { throw 'The clipboard does not contain an image.' }
    $file = Join-Path ([IO.Path]::GetTempPath()) "clipboard-$(Get-Date -Format yyyyMMdd-HHmmss).png"
    $img.Save($file, [System.Drawing.Imaging.ImageFormat]::Png)
    $img.Dispose()
    $file
}

# --- Resolve inputs -----------------------------------------------------------

if (-not $Repo) {
    $Repo = gh repo view --json nameWithOwner -q .nameWithOwner
    if ($LASTEXITCODE -ne 0 -or -not $Repo) { throw 'Not in a GitHub repo; pass -Repo owner/name.' }
}

$tempFiles = @()
if ($Clipboard) {
    $tempFiles += Get-ClipboardPng
    $Path = @($Path) + $tempFiles | Where-Object { $_ }
}
if (-not $Path) {
    $shots = Join-Path ([Environment]::GetFolderPath('MyPictures')) 'Screenshots'
    $latest = Get-ChildItem -LiteralPath $shots -File -ErrorAction SilentlyContinue |
        Where-Object Extension -In '.png', '.jpg', '.jpeg', '.gif', '.webp' |
        Sort-Object LastWriteTime -Descending | Select-Object -First 1
    if (-not $latest) { throw "No -Path given and no images found in $shots." }
    $Path = @($latest.FullName)
}

# --- Upload -------------------------------------------------------------------

try {
    Initialize-AssetsBranch -Repo $Repo -Branch $Branch

    $folder = if ($Issue) { "issue-$Issue" } else { 'unfiled' }
    $stamp = Get-Date -Format yyyyMMdd-HHmmss
    $markdown = foreach ($file in $Path) {
        $item = Get-Item -LiteralPath $file
        # Only a sanitized leaf name goes into the public path, never the local directory.
        $leaf = ($item.Name -replace '[^\w.\-]', '_')
        $remote = "$folder/$stamp-$leaf"
        Invoke-GhJson PUT "repos/$Repo/contents/$remote" @{
            message = if ($Issue) { "Screenshot for #$Issue" } else { 'Screenshot' }
            branch  = $Branch
            content = [Convert]::ToBase64String([IO.File]::ReadAllBytes($item.FullName))
        } | Out-Null
        "![$([IO.Path]::GetFileNameWithoutExtension($leaf))](https://github.com/$Repo/raw/$Branch/$remote)"
    }
} finally {
    $tempFiles | ForEach-Object { Remove-Item -LiteralPath $_ -ErrorAction SilentlyContinue }
}

$body = (@($Caption) + $markdown | Where-Object { $_ }) -join "`n`n"

if ($Issue -and -not $NoComment) {
    gh issue comment $Issue --repo $Repo --body $body
    if ($LASTEXITCODE -ne 0) { throw "Uploaded, but commenting on #$Issue failed. Markdown:`n$body" }
} else {
    $body
}
