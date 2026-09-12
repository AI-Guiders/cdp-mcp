# ADR-0222: point seat-local cdp-mcp.toml copies at the bridge SSOT (hardlink when possible).
# Usage: .\Sync-CdpConfigSsot.ps1 [-BridgeConfig D:\cdp-mcp\cdp-mcp.toml] [-SeatRoot D:\cdp-service]
[CmdletBinding()]
param(
    [string] $BridgeConfig = "",
    [string[]] $SeatRoots = @("D:\cdp-service", "D:\cdp-gatekeeper")
)

$ErrorActionPreference = "Stop"

if (-not $BridgeConfig) {
    $BridgeConfig = if ($env:CDP_MCP_CONFIG) { $env:CDP_MCP_CONFIG } else { "D:\cdp-mcp\cdp-mcp.toml" }
}

if (-not (Test-Path -LiteralPath $BridgeConfig)) {
    Write-Error "Bridge SSOT not found: $BridgeConfig"
}

$bridgeFull = (Resolve-Path -LiteralPath $BridgeConfig).Path

foreach ($seat in $SeatRoots) {
    if ([string]::IsNullOrWhiteSpace($seat)) { continue }
    if (-not (Test-Path -LiteralPath $seat)) {
        Write-Host "Skip missing seat: $seat"
        continue
    }

    $target = Join-Path $seat "cdp-mcp.toml"
    if ((Test-Path -LiteralPath $target) -and ((Resolve-Path -LiteralPath $target).Path -eq $bridgeFull)) {
        Write-Host "Already SSOT: $target"
        continue
    }

    if (Test-Path -LiteralPath $target) {
        Remove-Item -LiteralPath $target -Force
    }

    try {
        New-Item -ItemType HardLink -Path $target -Target $bridgeFull | Out-Null
        Write-Host "Hardlinked $target -> $bridgeFull"
    }
    catch {
        Copy-Item -LiteralPath $bridgeFull -Destination $target -Force
        Write-Warning "Hardlink failed ($($_.Exception.Message)); copied instead: $target"
    }
}
