# Recover Cursor MCP seat when tools say "Not connected" but bridge still runs.
# Escape hatch: run from terminal_* / external shell — never from in-proc cdp_shell_* while targeting self.
# Pattern: kill THAT seat's bridge exe (CdpMcpBridge.exe ADR-0198; legacy CdpMcp.exe) + optional apply pending bridge
# + per-seat CDP_RELOAD_NUDGE (kj-1349 / 0.5.661) + remount-wake pending (default; -NoStampRemountPending to skip).
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

    # Skip promote when cdp-pending-update.json has deferred bridge bits.
    [switch] $SkipApplyPending,

    # Escape: bump every CDP_RELOAD_NUDGE (pre-0.5.661 global thrash — avoid).
    [switch] $NudgeAllSeats
)

$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'CdpReloadNudge.ps1')

function Get-CdpSeatBridgeExeNames {
    # ADR-0198: Cursor stdio child is CdpMcpBridge.exe; monolith seat may still ship CdpMcp.exe.
    @('CdpMcpBridge.exe', 'CdpMcp.exe')
}

function Get-CdpPendingUpdatePath {
    Join-Path 'D:\cdp-service' 'cdp-pending-update.json'
}

function Resolve-CdpDeployWorkerExe {
    $found = @()
    $staging = 'D:\cdp-service.staging'
    if (Test-Path -LiteralPath $staging) {
        Get-ChildItem -LiteralPath $staging -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object {
                $p = Join-Path $_.FullName 'CdpMcp.exe'
                if (Test-Path -LiteralPath $p) { $found += $p }
            }
    }
    foreach ($root in @('D:\cdp-service', 'D:\cdp-mcp', 'D:\cdp-mcp-debug')) {
        $p = Join-Path $root 'CdpMcp.exe'
        if (Test-Path -LiteralPath $p) { $found += $p }
    }
    return $found | Select-Object -First 1
}

function Invoke-CdpApplyPendingBridge {
    param([string] $Reason = 'recover_seat')

    $pendingPath = Get-CdpPendingUpdatePath
    if (-not (Test-Path -LiteralPath $pendingPath)) {
        Write-Host 'Apply pending: skipped (no cdp-pending-update.json).'
        return @{ Ok = $true; Skipped = $true }
    }

    $worker = Resolve-CdpDeployWorkerExe
    if (-not $worker) {
        Write-Warning 'Apply pending: CdpMcp.exe deploy worker not found under service staging or install seats.'
        return @{ Ok = $false; Error = 'worker_not_found' }
    }

    $payloadPath = Join-Path $env:TEMP ("cdp-recover-apply-{0}.json" -f (Get-Date -Format 'yyyyMMddHHmmss'))
    '{"mode":"apply"}' | Set-Content -LiteralPath $payloadPath -Encoding utf8 -NoNewline

    try {
        if ($PSCmdlet.ShouldProcess($worker, "deploy-cli apply ($Reason)")) {
            Write-Host "Apply pending: $worker --deploy-cli $payloadPath"
            $stdout = & $worker --deploy-cli $payloadPath 2>&1 | Out-String
            $stdout.Trim() | Write-Host
            if ($stdout -match '"ok"\s*:\s*true') {
                return @{ Ok = $true; Worker = $worker; Output = $stdout.Trim() }
            }
            return @{ Ok = $false; Worker = $worker; Output = $stdout.Trim() }
        }
        return @{ Ok = $true; Skipped = $true; WhatIf = $true }
    }
    finally {
        Remove-Item -LiteralPath $payloadPath -Force -ErrorAction SilentlyContinue
    }
}

function Stop-CdpSeatBridgeProcesses {
    param(
        [string] $TargetRoot,
        [string[]] $ExeNames
    )

    $killed = @()
    foreach ($exeName in $ExeNames) {
        $exePath = Join-Path $TargetRoot $exeName
        $procs = Get-CimInstance Win32_Process -Filter "Name = '$exeName'" -ErrorAction SilentlyContinue |
            Where-Object {
                $_.ExecutablePath -and
                [string]::Equals(
                    [System.IO.Path]::GetFullPath($_.ExecutablePath),
                    [System.IO.Path]::GetFullPath($exePath),
                    [StringComparison]::OrdinalIgnoreCase)
            }
        foreach ($p in $procs) {
            if ($PSCmdlet.ShouldProcess("$($p.ExecutablePath) pid=$($p.ProcessId)", 'Stop-Process')) {
                Stop-Process -Id $p.ProcessId -Force -ErrorAction SilentlyContinue
                $killed += [pscustomobject]@{ Pid = $p.ProcessId; Path = $p.ExecutablePath }
                Write-Host "Killed pid=$($p.ProcessId) $($p.ExecutablePath)"
            }
        }
    }
    return $killed
}

if ($SoftFirst) {
    $NoKill = $true
}

if (-not $Target) {
    $Target = if ($Seat -eq 'cdp-debug') { 'D:\cdp-mcp-debug' } else { 'D:\cdp-mcp' }
}
$Target = [System.IO.Path]::GetFullPath($Target)
$bridgeExeNames = Get-CdpSeatBridgeExeNames

Write-Host 'Recover seat remount'
Write-Host "  Seat:   $Seat"
Write-Host "  Target: $Target"
Write-Host ("  Exes:   {0}" -f (($bridgeExeNames | ForEach-Object { Join-Path $Target $_ }) -join ', '))
if ($SoftFirst) { Write-Host '  Mode:   SoftFirst (nudge only — no kill)' }

$killed = @()
if (-not $NoKill) {
    $killed = @(Stop-CdpSeatBridgeProcesses -TargetRoot $Target -ExeNames $bridgeExeNames)
    if ($killed.Count -eq 0) {
        Write-Host 'No matching CdpMcpBridge.exe / CdpMcp.exe under Target (already dead or different seat).'
    }
    if (-not $SkipApplyPending) {
        Start-Sleep -Milliseconds 400
        $apply = Invoke-CdpApplyPendingBridge -Reason $(if ($killed.Count -gt 0) { 'recover_seat' } else { 'recover_apply_only' })
        if ($apply.Ok -and -not $apply.Skipped -and -not $apply.WhatIf) {
            Write-Host 'Apply pending: ok (bridge promote if staged).'
        }
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
    Write-Host 'If still Not connected after SoftFirst: re-run without -SoftFirst (kill + apply pending + nudge).'
}
