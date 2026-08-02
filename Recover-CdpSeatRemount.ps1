# Recover Cursor MCP seat when tools say "Not connected" but CdpMcp.exe still runs.
# Escape hatch: run from terminal_* / external shell — never from in-proc cdp_shell_* while targeting self.
# Pattern: KillRunning seat exe + CDP_RELOAD_NUDGE (kj-1349). Human Reload is last fallback.
#
# Examples:
#   pwsh -File Recover-CdpSeatRemount.ps1 -Seat cdp
#   pwsh -File Recover-CdpSeatRemount.ps1 -Target D:\cdp-mcp-debug -WhatIf

[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('cdp', 'cdp-debug')]
    [string] $Seat = 'cdp',

    [string] $Target,

    [switch] $NoNudgeMcp,

    [switch] $NoKill,

    [switch] $StampRemountPending
)

$ErrorActionPreference = 'Stop'

if (-not $Target) {
    $Target = if ($Seat -eq 'cdp-debug') { 'D:\cdp-mcp-debug' } else { 'D:\cdp-mcp' }
}
$Target = [System.IO.Path]::GetFullPath($Target)
$exeName = 'CdpMcp.exe'
$exePath = Join-Path $Target $exeName

function Invoke-CdpReloadNudge {
    $mcpJson = Join-Path $env:USERPROFILE '.cursor\mcp.json'
    if (-not (Test-Path -LiteralPath $mcpJson)) {
        return @{ Ok = $false; Error = "missing $mcpJson" }
    }
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $raw = Get-Content -LiteralPath $mcpJson -Raw -Encoding utf8
    if ($raw -notmatch '"CDP_RELOAD_NUDGE"') {
        return @{ Ok = $false; Error = 'no CDP_RELOAD_NUDGE keys in mcp.json' }
    }
    $count = ([regex]::Matches($raw, '"CDP_RELOAD_NUDGE"\s*:')).Count
    $next = [regex]::Replace(
        $raw,
        '"CDP_RELOAD_NUDGE"\s*:\s*"[^"]*"',
        "`"CDP_RELOAD_NUDGE`": `"$stamp`"")
    if ($next -eq $raw) {
        return @{ Ok = $false; Error = 'replace produced no change' }
    }
    Set-Content -LiteralPath $mcpJson -Value $next -Encoding utf8 -NoNewline
    return @{ Ok = $true; Path = $mcpJson; Value = $stamp; Count = $count }
}

function Write-CdpRemountWakePending([string]$TargetRoot) {
    $full = [System.IO.Path]::GetFullPath($TargetRoot)
    $leaf = [System.IO.Path]::GetFileName($full.TrimEnd('\', '/'))
    $seatName = if ($leaf -ieq 'cdp-mcp-debug') { 'cdp-debug' }
                elseif ($leaf -ieq 'cdp-mcp') { 'cdp' }
                else { 'other' }
    $dir = Join-Path $env:LOCALAPPDATA 'cdp-mcp'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $path = Join-Path $dir "remount-wake-$seatName.pending.json"
    $doc = [ordered]@{
        schema      = 'remount_wake/v1'
        seat        = $seatName
        target      = $full
        reason      = 'recover_seat'
        stamped_utc = (Get-Date).ToUniversalTime().ToString('o')
    }
    ($doc | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $path -Encoding utf8
    return @{ Ok = $true; Path = $path; Seat = $seatName }
}

Write-Host "Recover seat remount"
Write-Host "  Seat:   $Seat"
Write-Host "  Target: $Target"
Write-Host "  Exe:    $exePath"

$killed = @()
if (-not $NoKill) {
    $procs = Get-CimInstance Win32_Process -Filter "Name = '$exeName'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ExecutablePath -and
            ([System.IO.Path]::GetFullPath($_.ExecutablePath)).StartsWith($Target, [StringComparison]::OrdinalIgnoreCase)
        }
    foreach ($p in $procs) {
        if ($PSCmdlet.ShouldProcess("$($p.ExecutablePath) pid=$($p.ProcessId)", 'Stop-Process')) {
            Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
            $killed += [pscustomobject]@{ Pid = $p.ProcessId; Path = $p.ExecutablePath }
            Write-Host "Killed pid=$($p.ProcessId) $($p.ExecutablePath)"
        }
    }
    if ($killed.Count -eq 0) {
        Write-Host 'No matching CdpMcp.exe under Target (already dead or different seat).'
    }
}

if ($StampRemountPending) {
    $pending = Write-CdpRemountWakePending $Target
    if ($pending.Ok) {
        Write-Host "Remount pending: $($pending.Path) seat=$($pending.Seat)"
    }
}

if (-not $NoNudgeMcp) {
    if ($PSCmdlet.ShouldProcess((Join-Path $env:USERPROFILE '.cursor\mcp.json'), 'Bump CDP_RELOAD_NUDGE')) {
        $nudge = Invoke-CdpReloadNudge
        if ($nudge.Ok) {
            Write-Host "MCP nudge: $($nudge.Path) CDP_RELOAD_NUDGE=$($nudge.Value) (x$($nudge.Count))"
        }
        else {
            Write-Warning "Nudge failed: $($nudge.Error)"
        }
    }
}

Write-Host ''
Write-Host 'Next: wait for Cursor MCP remount (or human Reload). Then cdp_health + cdp_pressure op=recall.'
