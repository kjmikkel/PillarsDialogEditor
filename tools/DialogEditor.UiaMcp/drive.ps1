# Manual verification driver for the MCP server. Speaks newline-delimited JSON-RPC
# over stdio, which is how an MCP client talks to it. Use to smoke-test tools
# without wiring the server into a client.
#
#   pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool session_status
#
# -Arguments takes a hashtable when the script is CALLED from an existing pwsh session:
#   & tools/DialogEditor.UiaMcp/drive.ps1 -Tool launch_app -Arguments @{ repoRoot = $pwd.Path }
#
# ...or a JSON STRING when starting a new pwsh process, because a hashtable passed as a
# command-line argument is stringified to "System.Collections.Hashtable" and lost:
#   pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool launch_app -Arguments '{"repoRoot":"C:/repo"}'
#
# Pass -Then to run further tools against the SAME server process, which is how you
# verify that a session survives across separate tool calls:
#   ... -Tool launch_app -Arguments '{"repoRoot":"C:/repo"}' -Then session_status,kill_app

param(
    [string]$Tool,
    # Hashtable (in-session) or JSON string (across a process boundary) — see header.
    [object]$Arguments = @{},
    # Comma-separated is accepted, because a string[] passed across a new pwsh
    # process boundary arrives as one comma-joined string. -Then carries no arguments;
    # use -Script when later calls in the same session need their own.
    [string[]]$Then = @(),
    # A JSON array of {name, arguments} run in order against ONE server process, for
    # sequences where later tools need arguments of their own:
    #   -Script '[{"name":"launch_app","arguments":{"repoRoot":"C:/repo"}},
    #             {"name":"find","arguments":{"query":"Viewbox"}}]'
    [string]$Script,
    [switch]$KeepAlive,
    [string]$Exe = "tools/DialogEditor.UiaMcp/bin/Debug/net8.0-windows/DialogEditor.UiaMcp.exe",
    [int]$TimeoutSec = 120
)
$ErrorActionPreference = 'Stop'

# Resolve to an absolute path first. PowerShell's current location is NOT .NET's
# Environment.CurrentDirectory, so handing a relative FileName to ProcessStartInfo
# resolves it against the wrong directory and fails with "file not found".
$ExePath = (Resolve-Path -LiteralPath $Exe).Path

$Then = @($Then | ForEach-Object { $_ -split ',' } | Where-Object { $_ })

if ($Arguments -is [string]) {
    $Arguments = if ([string]::IsNullOrWhiteSpace($Arguments)) { @{} }
                 else { $Arguments | ConvertFrom-Json -AsHashtable }
}

$psi = [System.Diagnostics.ProcessStartInfo]::new($ExePath)
$psi.WorkingDirectory = (Get-Location).Path
$psi.RedirectStandardInput = $true
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.UseShellExecute = $false
$proc = [System.Diagnostics.Process]::Start($psi)

$script:NextId = 0

function Send-Rpc {
    param([object]$Id, [string]$Method, $Params)
    $msg = @{ jsonrpc = "2.0"; method = $Method }
    if ($null -ne $Id)     { $msg.id = $Id }
    if ($null -ne $Params) { $msg.params = $Params }
    $proc.StandardInput.WriteLine(($msg | ConvertTo-Json -Depth 10 -Compress))
    $proc.StandardInput.Flush()
    if ($null -eq $Id) { return $null }

    $deadline = (Get-Date).AddSeconds($TimeoutSec)
    while ((Get-Date) -lt $deadline) {
        $line = $proc.StandardOutput.ReadLine()
        if (-not $line) { continue }
        try { $o = $line | ConvertFrom-Json } catch { continue }
        if ($o.id -eq $Id) { return $o }
    }
    throw "timeout waiting for $Method"
}

function Invoke-Tool {
    # NOT named $Args: that is a PowerShell automatic variable holding the current
    # scope's unbound arguments, so a parameter of that name resolves to an empty
    # ARRAY and serialises as [] — which the server rejects, since MCP tool
    # arguments must be a JSON object.
    param([string]$Name, [hashtable]$ToolArgs = @{})
    $script:NextId++
    $resp = Send-Rpc -Id $script:NextId -Method "tools/call" -Params @{ name = $Name; arguments = $ToolArgs }
    Write-Host "=== $Name ==="
    if ($resp.error) { Write-Host "ERROR: $($resp.error.message)" }
    else { ($resp.result.content | Where-Object { $_.type -eq 'text' }).text | Write-Host }
}

try {
    $script:NextId++
    Send-Rpc -Id $script:NextId -Method "initialize" -Params @{
        protocolVersion = "2024-11-05"; capabilities = @{}
        clientInfo = @{ name = "drive.ps1"; version = "1" } } | Out-Null
    Send-Rpc -Id $null -Method "notifications/initialized" -Params @{} | Out-Null

    $ranKill = $false
    if ($Script) {
        foreach ($step in ($Script | ConvertFrom-Json)) {
            $stepArgs = @{}
            if ($step.arguments) {
                $step.arguments.PSObject.Properties | ForEach-Object { $stepArgs[$_.Name] = $_.Value }
            }
            Invoke-Tool -Name $step.name -ToolArgs $stepArgs
            if ($step.name -eq 'kill_app') { $ranKill = $true }
        }
    }
    else {
        if (-not $Tool) { throw "Pass -Tool or -Script." }
        Invoke-Tool -Name $Tool -ToolArgs $Arguments
        foreach ($t in $Then) { Invoke-Tool -Name $t -ToolArgs @{} }
        $ranKill = ($Tool -eq 'kill_app') -or ($Then -contains 'kill_app')
    }

    # Never leave the editor running with the user's settings mutated. A hard Kill()
    # of this server does NOT run its ProcessExit handler, so nothing else would
    # tear the app down or restore settings until the next server start.
    if (-not $KeepAlive -and -not $ranKill) { Invoke-Tool -Name 'kill_app' }
}
finally {
    # Close stdin so the server ends its stdio loop and exits normally, giving its
    # ProcessExit handler a chance to run; only kill if it will not go quietly.
    try { $proc.StandardInput.Close() } catch { }
    if (-not $proc.WaitForExit(5000)) { $proc.Kill(); $proc.WaitForExit(3000) | Out-Null }
    $err = $proc.StandardError.ReadToEnd()
    if ($err) {
        $lines = @($err -split "`n" | Where-Object { $_ -match 'fail|error|Exception|restore' })
        if ($lines) { Write-Host "--- server stderr (filtered) ---"; $lines | Select-Object -Last 8 | Write-Host }
    }
}
