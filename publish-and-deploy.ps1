# CDP deploy — escape hatch wrapper (SSOT: Cdp.Deploy / IdeDeploy in C#).
# Prefer: cdp_deploy mode=… from MCP (durable worker). This script resolves a worker exe and forwards JSON args.
[CmdletBinding()]
param(
    [ValidateSet("soft", "hard", "apply")]
    [string] $Mode = "soft",
    [string] $ServiceTarget = "D:\cdp-service",
    [string] $BridgeTarget = "D:\cdp-mcp",
    [string] $BridgeDebugTarget = "D:\cdp-mcp-debug",
    [switch] $UseNuGet,
    [switch] $NoNudgeMcp
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot

function Find-WorkerExe {
    foreach ($root in @($ServiceTarget, $BridgeTarget, $BridgeDebugTarget)) {
        foreach ($name in @("CdpService.exe", "CdpMcp.exe")) {
            $candidate = Join-Path $root $name
            if (Test-Path -LiteralPath $candidate) { return $candidate }
        }
    }
    $built = Join-Path $here "bin\Release\net10.0\win-x64\CdpMcp.exe"
    if (Test-Path -LiteralPath $built) { return $built }
    throw "CdpMcp/CdpService worker not found. Build cdp-mcp or deploy service first."
}

$payload = [ordered]@{
    mode = $Mode
    target = $BridgeTarget
    use_nuget = [bool]$UseNuGet
    no_nudge = [bool]$NoNudgeMcp
}
$payloadPath = Join-Path $env:TEMP ("cdp-deploy-" + [Guid]::NewGuid().ToString("N") + ".json")
($payload | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $payloadPath -Encoding utf8

$worker = Find-WorkerExe
Write-Host "CDP deploy ($Mode) via C# worker: $worker"
& $worker --deploy-cli $payloadPath
exit $LASTEXITCODE
