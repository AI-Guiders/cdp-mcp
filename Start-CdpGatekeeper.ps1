# Ensure CdpGatekeeper tower is listening (ADR-0209). Idempotent.
# Usage: .\Start-CdpGatekeeper.ps1 [-Target D:\cdp-gatekeeper] [-Config D:\cdp-mcp\cdp-mcp.toml] [-Port 8771]
[CmdletBinding()]
param(
    [string] $Target = "D:\cdp-gatekeeper",
    [string] $Config = "",
    [int] $Port = 8771
)

$ErrorActionPreference = "Stop"

function Test-GatekeeperPort {
    param([int] $ListenPort)
    try {
        $client = New-Object System.Net.Sockets.TcpClient
        $client.Connect("127.0.0.1", $ListenPort)
        $client.Close()
        return $true
    }
    catch {
        return $false
    }
}

$exe = Join-Path $Target "CdpGatekeeper.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    $fallbackRoot = "D:\cdp-service"
    $fallback = Join-Path $fallbackRoot "CdpGatekeeper.exe"
    if (Test-Path -LiteralPath $fallback) {
        $exe = $fallback
        $Target = $fallbackRoot
    }
    else {
        Write-Error "CdpGatekeeper.exe not found under $Target (or $fallbackRoot)"
    }
}

if (-not $Config) {
    $Config = "D:\cdp-mcp\cdp-mcp.toml"
    if (-not (Test-Path -LiteralPath $Config)) {
        $Config = Join-Path $Target "cdp-mcp.toml"
    }
}

if (Test-GatekeeperPort -ListenPort $Port) {
    Write-Host "CdpGatekeeper already listening: 127.0.0.1:$Port"
    exit 0
}

Write-Host "Starting CdpGatekeeper: $exe (config: $Config)"
$startArgs = @("--gatekeeper", "--config", $Config)
Start-Process -FilePath $exe -ArgumentList $startArgs -WindowStyle Hidden -WorkingDirectory $Target | Out-Null

for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 500
    if (Test-GatekeeperPort -ListenPort $Port) {
        Write-Host "CdpGatekeeper ready: 127.0.0.1:$Port"
        exit 0
    }
}

Write-Error "CdpGatekeeper did not bind $Port within 15s"
