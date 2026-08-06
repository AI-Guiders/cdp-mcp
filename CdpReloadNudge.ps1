# Shared MCP remount nudge — bump CDP_RELOAD_NUDGE only for named Cursor servers.
# Global replace of every nudge key remounted BOTH seats on every hard sibling deploy (pre-0.5.661).
# Dot-source from publish-and-deploy.ps1 / Recover-CdpSeatRemount.ps1.
# Direct: pwsh -File CdpReloadNudge.ps1 -Server cdp|cdp-debug  (or -AllSeats escape).
# Lived 2026-08-06: -File alone was a silent no-op (functions only) → Not connected until Invoke-.

param(
    [Alias('Seat')]
    [string[]] $Server = @(),
    [Alias('NudgeAllSeats')]
    [switch] $AllSeats
)

function Resolve-CdpMcpServerName {
    param(
        [string] $Seat,
        [string] $TargetRoot
    )
    if ($Seat -eq 'cdp-debug' -or $Seat -eq 'debug') { return 'cdp-debug' }
    if ($Seat -eq 'cdp' -or $Seat -eq 'release') { return 'cdp' }
    if ($TargetRoot) {
        $full = [System.IO.Path]::GetFullPath($TargetRoot)
        $leaf = [System.IO.Path]::GetFileName($full.TrimEnd('\', '/'))
        if ($leaf -ieq 'cdp-mcp-debug') { return 'cdp-debug' }
        if ($leaf -ieq 'cdp-mcp') { return 'cdp' }
    }
    return $null
}

function Invoke-CdpReloadNudge {
    param(
        # Cursor mcpServers keys to bump. Empty + -AllSeats = legacy global (escape).
        [string[]] $Server = @(),
        [switch] $AllSeats
    )

    $mcpJson = Join-Path $env:USERPROFILE '.cursor\mcp.json'
    if (-not (Test-Path -LiteralPath $mcpJson)) {
        return @{ Ok = $false; Error = "missing $mcpJson" }
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $raw = Get-Content -LiteralPath $mcpJson -Raw -Encoding utf8
    if ($raw -notmatch '"CDP_RELOAD_NUDGE"') {
        return @{ Ok = $false; Error = 'no CDP_RELOAD_NUDGE keys in mcp.json' }
    }

    if ($AllSeats -or ($Server.Count -eq 0)) {
        if (-not $AllSeats -and $Server.Count -eq 0) {
            return @{ Ok = $false; Error = 'Server= required (or -AllSeats escape)' }
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
        return @{ Ok = $true; Path = $mcpJson; Value = $stamp; Count = $count; Servers = @('*') }
    }

    try {
        $j = $raw | ConvertFrom-Json
    } catch {
        return @{ Ok = $false; Error = "mcp.json parse failed: $($_.Exception.Message)" }
    }

    if (-not $j.mcpServers) {
        return @{ Ok = $false; Error = 'mcp.json has no mcpServers' }
    }

    $bumped = @()
    foreach ($name in $Server) {
        $key = [string]$name
        if (-not $key) { continue }
        $node = $j.mcpServers.$key
        if (-not $node) {
            return @{ Ok = $false; Error = "mcpServers.$key missing" }
        }
        if (-not $node.env) {
            $node | Add-Member -NotePropertyName env -NotePropertyValue ([pscustomobject]@{}) -Force
        }
        $envObj = $node.env
        if ($envObj.PSObject.Properties.Name -contains 'CDP_RELOAD_NUDGE') {
            $envObj.CDP_RELOAD_NUDGE = $stamp
        } else {
            $envObj | Add-Member -NotePropertyName CDP_RELOAD_NUDGE -NotePropertyValue $stamp -Force
        }
        $bumped += $key
    }

    if ($bumped.Count -eq 0) {
        return @{ Ok = $false; Error = 'no servers bumped' }
    }

    $json = $j | ConvertTo-Json -Depth 30
    [System.IO.File]::WriteAllText($mcpJson, $json)
    return @{ Ok = $true; Path = $mcpJson; Value = $stamp; Count = $bumped.Count; Servers = $bumped }
}

function Resolve-CdpRemountSeatName {
    param([string] $TargetRoot)
    $full = [System.IO.Path]::GetFullPath($TargetRoot)
    $leaf = [System.IO.Path]::GetFileName($full.TrimEnd('\', '/'))
    if ($leaf -ieq 'self') {
        $parent = [System.IO.Path]::GetDirectoryName($full)
        if ($parent) {
            return Resolve-CdpRemountSeatName -TargetRoot $parent
        }
    }
    if ($leaf -ieq 'cdp-mcp-debug') { return 'cdp-debug' }
    if ($leaf -ieq 'cdp-mcp') { return 'cdp' }
    if ($full -ieq ([System.IO.Path]::GetFullPath('D:\cdp-mcp-debug'))) { return 'cdp-debug' }
    if ($full -ieq ([System.IO.Path]::GetFullPath('D:\cdp-mcp'))) { return 'cdp' }
    return 'other'
}

function Write-CdpRemountWakePending {
    param(
        [string] $TargetRoot,
        [string] $Reason = 'hard_deploy'
    )
    $full = [System.IO.Path]::GetFullPath($TargetRoot)
    $seatName = Resolve-CdpRemountSeatName -TargetRoot $full
    $dir = Join-Path $env:LOCALAPPDATA 'cdp-mcp'
    New-Item -ItemType Directory -Force -Path $dir | Out-Null
    $path = Join-Path $dir "remount-wake-$seatName.pending.json"
    $doc = [ordered]@{
        schema      = 'remount_wake/v1'
        seat        = $seatName
        target      = $full
        reason      = $Reason
        stamped_utc = (Get-Date).ToUniversalTime().ToString('o')
    }
    ($doc | ConvertTo-Json -Depth 4) | Set-Content -LiteralPath $path -Encoding utf8
    return @{ Ok = $true; Path = $path; Seat = $seatName }
}

# Entry when executed as -File (not when Recover/publish dot-source this library).
if ($PSCommandPath -and $MyInvocation.MyCommand.Path -and
    ([System.IO.Path]::GetFullPath($PSCommandPath) -eq [System.IO.Path]::GetFullPath($MyInvocation.MyCommand.Path))) {
    $r = Invoke-CdpReloadNudge -Server $Server -AllSeats:$AllSeats
    if (-not $r.Ok) {
        Write-Error ($r.Error ?? 'nudge failed')
        exit 1
    }
    Write-Output ("nudge ok · servers={0} · CDP_RELOAD_NUDGE={1}" -f (($r.Servers -join ','), $r.Value))
    exit 0
}
