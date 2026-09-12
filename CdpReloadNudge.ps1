# Shared MCP remount nudge — bump --bridge-rev in args (ADR-0224). No env.
# Legacy CDP_RELOAD_NUDGE in env is migrated away on bump.
# Direct: pwsh -File CdpReloadNudge.ps1 -Server cdp|cdp-debug

param(
    [Alias('Seat')]
    [string[]] $Server = @(),
    [Alias('NudgeAllSeats')]
    [switch] $AllSeats
)

function Set-BridgeRevArgs {
    param(
        [psobject] $Node,
        [string] $Stamp
    )
    $argsList = @()
    if ($Node.args) { $argsList = @($Node.args) }
    $idx = [array]::IndexOf($argsList, '--bridge-rev')
    if ($idx -ge 0 -and ($idx + 1) -lt $argsList.Count) {
        $argsList[$idx + 1] = $Stamp
    }
    else {
        $argsList += @('--bridge-rev', $Stamp)
    }
    $Node.args = $argsList
    if ($Node.env -and ($Node.env.PSObject.Properties.Name -contains 'CDP_RELOAD_NUDGE')) {
        $Node.env.PSObject.Properties.Remove('CDP_RELOAD_NUDGE')
        if ($Node.env.PSObject.Properties.Count -eq 0) { $Node.PSObject.Properties.Remove('env') }
    }
}

function Invoke-CdpReloadNudge {
    param(
        [string[]] $Server = @(),
        [switch] $AllSeats
    )

    $mcpJson = Join-Path $env:USERPROFILE '.cursor\mcp.json'
    if (-not (Test-Path -LiteralPath $mcpJson)) {
        return @{ Ok = $false; Error = "missing $mcpJson" }
    }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    try {
        $j = Get-Content -LiteralPath $mcpJson -Raw -Encoding utf8 | ConvertFrom-Json
    }
    catch {
        return @{ Ok = $false; Error = "mcp.json parse failed: $($_.Exception.Message)" }
    }

    if (-not $j.mcpServers) {
        return @{ Ok = $false; Error = 'mcp.json has no mcpServers' }
    }

    $targets = @()
    if ($AllSeats) {
        $j.mcpServers.PSObject.Properties | ForEach-Object {
            if ($_.Value.args -contains '--config' -or $_.Value.env.CDP_RELOAD_NUDGE) {
                $targets += $_.Name
            }
        }
    }
    elseif ($Server.Count -gt 0) {
        $targets = @($Server)
    }
    else {
        return @{ Ok = $false; Error = 'Server= required (or -AllSeats escape)' }
    }

    $bumped = @()
    foreach ($name in $targets) {
        $node = $j.mcpServers.$name
        if (-not $node) {
            return @{ Ok = $false; Error = "mcpServers.$name missing" }
        }
        Set-BridgeRevArgs -Node $node -Stamp $stamp
        $bumped += $name
    }

    if ($bumped.Count -eq 0) {
        return @{ Ok = $false; Error = 'no servers bumped' }
    }

    ($j | ConvertTo-Json -Depth 30) | Set-Content -LiteralPath $mcpJson -Encoding utf8 -NoNewline
    return @{ Ok = $true; Path = $mcpJson; Value = $stamp; Count = $bumped.Count; Servers = $bumped }
}

function Resolve-CdpRemountSeatName {
    param([string] $TargetRoot)
    $full = [System.IO.Path]::GetFullPath($TargetRoot)
    $leaf = [System.IO.Path]::GetFileName($full.TrimEnd('\', '/'))
    if ($leaf -ieq 'self') {
        $parent = [System.IO.Path]::GetDirectoryName($full)
        if ($parent) { return Resolve-CdpRemountSeatName -TargetRoot $parent }
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

if ($MyInvocation.InvocationName -ne '.') {
    $r = Invoke-CdpReloadNudge -Server $Server -AllSeats:$AllSeats
    if (-not $r.Ok) {
        $err = if ($null -ne $r.Error -and $r.Error -ne '') { $r.Error } else { 'nudge failed' }
        Write-Error $err
        exit 1
    }
    Write-Output ("nudge ok · servers={0} · --bridge-rev={1}" -f (($r.Servers -join ','), $r.Value))
    exit 0
}
