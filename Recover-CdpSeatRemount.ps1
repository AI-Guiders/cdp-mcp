# Recover Cursor MCP seat when tools say "Not connected" but CdpMcp.exe still runs.
# Escape hatch: run from terminal_* / external shell — never from in-proc cdp_shell_* while targeting self.
# Pattern: kill THAT seat's CdpMcp.exe (exact exe path) + per-seat CDP_RELOAD_NUDGE (kj-1349 / 0.5.661)
# + remount-wake pending (default; -NoStampRemountPending to skip). Human Reload is last fallback.
# Never match by StartsWith(Target): D:\cdp-mcp-debug starts with D:\cdp-mcp → sibling kill thrash.
#
# Soft path (FDR 2026-08-05): hung wire often leaves process alive — try -SoftFirst (nudge only, no kill)
# first; escalate to full Recover if still Not connected after remount.
#
# Examples:
#   pwsh -File Recover-CdpSeatRemount.ps1 -Seat cdp
#   pwsh -File Recover-CdpSeatRemount.ps1 -Seat cdp -SoftFirst
#   pwsh -File Recover-CdpSeatRemount.ps1 -Target D:\cdp-mcp-debug -WhatIf

[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('cdp', 'cdp-debug')]
    [string] $Seat = 'cdp',

    [string] $Target,

    [switch] $NoNudgeMcp,

    [switch] $NoKill,

    # Nudge + remount pending only — do not KillRunning (prefer when process may still be healthy).
    [switch] $SoftFirst,

    [switch] $NoStampRemountPending,

    # Escape: bump every CDP_RELOAD_NUDGE (pre-0.5.661 global thrash — avoid).
    [switch] $NudgeAllSeats
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'CdpReloadNudge.ps1')

if ($SoftFirst) {
    $NoKill = $true
}

if (-not $Target) {
    $Target = if ($Seat -eq 'cdp-debug') { 'D:\cdp-mcp-debug' } else { 'D:\cdp-mcp' }
}
$Target = [System.IO.Path]::GetFullPath($Target)
$exeName = 'CdpMcp.exe'
$exePath = Join-Path $Target $exeName

Write-Host "Recover seat remount"
Write-Host "  Seat:   $Seat"
Write-Host "  Target: $Target"
Write-Host "  Exe:    $exePath"
if ($SoftFirst) { Write-Host '  Mode:   SoftFirst (nudge only — no kill)' }

$killed = @()
if (-not $NoKill) {
    $procs = Get-CimInstance Win32_Process -Filter "Name = '$exeName'" -ErrorAction SilentlyContinue |
        Where-Object {
            $_.ExecutablePath -and
            # Exact exe path — NOT StartsWith($Target): D:\cdp-mcp-debug starts with D:\cdp-mcp (sibling kill thrash).
            [string]::Equals(
                [System.IO.Path]::GetFullPath($_.ExecutablePath),
                $exePath,
                [StringComparison]::OrdinalIgnoreCase)
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
elseif ($SoftFirst) {
    Write-Host 'SoftFirst: skipped KillRunning (process left alive).'
}

if (-not $NoStampRemountPending) {
    $pending = Write-CdpRemountWakePending -TargetRoot $Target -Reason $(if ($SoftFirst) { 'recover_soft' } else { 'recover_seat' })
    if ($pending.Ok) {
        Write-Host "Remount pending: $($pending.Path) seat=$($pending.Seat)"
    }
}

if (-not $NoNudgeMcp) {
    if ($PSCmdlet.ShouldProcess((Join-Path $env:USERPROFILE '.cursor\mcp.json'), 'Bump CDP_RELOAD_NUDGE')) {
        if ($NudgeAllSeats) {
            $nudge = Invoke-CdpReloadNudge -AllSeats
        } else {
            $nudge = Invoke-CdpReloadNudge -Server $Seat
        }
        if ($nudge.Ok) {
            $who = if ($nudge.Servers) { ($nudge.Servers -join ',') } else { $Seat }
            Write-Host "MCP nudge: $($nudge.Path) seat=$who CDP_RELOAD_NUDGE=$($nudge.Value) (x$($nudge.Count))"
        }
        else {
            Write-Warning "Nudge failed: $($nudge.Error)"
        }
    }
}

Write-Host ''
Write-Host 'Next: wait for Cursor MCP remount (or human Reload). Then cdp_health + cdp_pressure op=recall.'
Write-Host '  Tip: CallTool timeout while ListTools ready = remount Composer wake mid SoftFL ship (Cursor MCP CallTool zombie). Prefer -NoStampRemountPending (or SoftFirst first); SoftFL ACCEPT: remount wake suppress under any last_once insurance (not invent-only only).' -ForegroundColor DarkYellow
if ($SoftFirst) {
    Write-Host 'If still Not connected after SoftFirst: re-run without -SoftFirst (kill + nudge).'
}
