# Ensure durable CdpService is running (ADR-0198 sidecar).
# Usage: .\Start-CdpService.ps1 [-Target D:\cdp-service] [-Config D:\cdp-service\cdp-mcp.toml]
[CmdletBinding()]
param(
    [string] $Target = "D:\cdp-service",
    [string] $Config = ""
)

$ErrorActionPreference = "Stop"
$exe = Join-Path $Target "CdpService.exe"
if (-not (Test-Path -LiteralPath $exe)) {
    $fallback = Join-Path $Target "CdpMcp.exe"
    if (Test-Path -LiteralPath $fallback) { $exe = $fallback }
    else { Write-Error "CdpService.exe not found under $Target" }
}

if (-not $Config) {
    $Config = Join-Path $Target "cdp-mcp.toml"
}

$healthUrl = "http://127.0.0.1:8771/healthz"
try {
    $r = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2
    if ($r.StatusCode -eq 200) {
        Write-Host "CdpService already up: $healthUrl"
        exit 0
    }
} catch { }

Write-Host "Starting CdpService: $exe"
$args = @("--service", "--config", $Config)
Start-Process -FilePath $exe -ArgumentList $args -WindowStyle Hidden -WorkingDirectory $Target | Out-Null

for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Milliseconds 500
    try {
        $r = Invoke-WebRequest -Uri $healthUrl -UseBasicParsing -TimeoutSec 2
        if ($r.StatusCode -eq 200) {
            Write-Host "CdpService ready: $healthUrl"
            exit 0
        }
    } catch { }
}

Write-Error "CdpService did not become healthy within 15s"
