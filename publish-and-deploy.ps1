# Publish Release (win-x64, self-contained) — ADR-0198 sidecar layout.
# Run from EXTERNAL terminal only (not cdp_shell_*).
#
#   D:\cdp-service\   CdpService.exe — durable substrate (KillRunning target)
#   D:\cdp-mcp\       CdpMcpBridge.exe — Cursor stdio child (cheap remount)
[CmdletBinding()]
param(
    [string] $BridgeTarget = "D:\cdp-mcp",
    [string] $BridgeDebugTarget = "D:\cdp-mcp-debug",
    [string] $ServiceTarget = "D:\cdp-service",
    [string] $Target = "",
    [ValidateSet("soft", "hard", "apply")]
    [string] $Mode = "soft",
    [switch] $UseNuGet,
    [switch] $NoNudgeMcp,
    [switch] $NudgeAllSeats
)

if ($Target) {
    Write-Warning "-Target is deprecated; mapped to -BridgeTarget (ADR-0198)."
    if (-not $PSBoundParameters.ContainsKey('BridgeTarget')) { $BridgeTarget = $Target }
}

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$serviceCsproj = Join-Path $here "CdpMcp.csproj"
$bridgeCsproj = Join-Path $here "CdpMcpBridge\CdpMcpBridge.csproj"
$preserveConfig = Join-Path $here "aid-publish.toml"
if (-not (Test-Path -LiteralPath $serviceCsproj)) { Write-Error "Missing $serviceCsproj"; exit 1 }
if (-not (Test-Path -LiteralPath $bridgeCsproj)) { Write-Error "Missing $bridgeCsproj"; exit 1 }

. (Join-Path $here "CdpReloadNudge.ps1")

function Invoke-CdpBridgeDeployNudge {
    param(
        [string] $BridgeTarget,
        [string] $BridgeDebugTarget,
        [switch] $NudgeAllSeats
    )
    if ($NudgeAllSeats) {
        return Invoke-CdpReloadNudge -AllSeats
    }
    $servers = @('cdp')
    if ($BridgeDebugTarget -and ($BridgeDebugTarget -ne $BridgeTarget)) {
        $servers += 'cdp-debug'
    }
    return Invoke-CdpReloadNudge -Server $servers
}

function Publish-CdpProject {
    param(
        [string] $Project,
        [string] $DeployRoot,
        [switch] $KillRunning
    )
    $publishArgs = @(
        "-Project", $Project,
        "-Target", $DeployRoot,
        "-Runtime", "win-x64",
        "-Configuration", "Release",
        "-SelfContained"
    )
    if ($KillRunning) { $publishArgs += "-KillRunning" }
    if ($UseNuGet) { $publishArgs += "-UseNuGet" }
    if (Test-Path -LiteralPath $preserveConfig) {
        $publishArgs += "-PreserveConfig", $preserveConfig
    }

    $aidPublish = Get-Command aid-publish -ErrorAction SilentlyContinue
    if ($aidPublish) {
        & $aidPublish.Source @publishArgs
    } elseif (Test-Path -LiteralPath (Join-Path $here ".config\dotnet-tools.json")) {
        Push-Location $here
        try {
            & dotnet tool restore | Out-Null
            & dotnet tool run aid-publish -- @publishArgs
        } finally {
            Pop-Location
        }
    } else {
        Write-Error "aid-publish not found. Install: dotnet tool install -g aiguiders.dotnettools.publishfixedtarget"
        exit 1
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

function Copy-TsWorker {
    param([string] $DeployRoot)
    $workerSrc = Join-Path $here "..\typescript-lang\worker"
    $workerDst = Join-Path $DeployRoot "ts-worker"
    if (-not (Test-Path -LiteralPath (Join-Path $workerSrc "index.mjs"))) {
        # Same soft gap as package-win-release.ps1 — TS facet optional; core CdpService must still ship.
        Write-Host "WARN: typescript-lang worker missing ($workerSrc\index.mjs) — TS facet empty; core deploy continues" -ForegroundColor Yellow
        return
    }
    if (-not (Test-Path -LiteralPath (Join-Path $workerSrc "node_modules\typescript"))) {
        Write-Host "npm install in ts-worker source..."
        Push-Location $workerSrc
        try {
            & npm install --omit=dev
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        } finally { Pop-Location }
    }
    if (Test-Path -LiteralPath $workerDst) { Remove-Item -LiteralPath $workerDst -Recurse -Force }
    Copy-Item -LiteralPath $workerSrc -Destination $workerDst -Recurse -Force
}

function Ensure-Config {
    param(
        [string] $DeployRoot,
        [string] $FallbackConfig = ""
    )
    $configSrc = Join-Path $here "config\cdp-mcp.toml"
    if (-not (Test-Path -LiteralPath $configSrc)) { Write-Error "Missing config template: $configSrc"; exit 1 }
    $configDst = Join-Path $DeployRoot "cdp-mcp.toml"
    if (Test-Path -LiteralPath $configDst) {
        Write-Host "Keep existing config: $configDst"
    } elseif ($FallbackConfig -and (Test-Path -LiteralPath $FallbackConfig)) {
        Copy-Item -LiteralPath $FallbackConfig -Destination $configDst -Force
        Write-Host "Copied config from fallback: $configDst"
    } else {
        Copy-Item -LiteralPath $configSrc -Destination $configDst -Force
        Write-Host "Seeded config: $configDst"
    }
    return $configDst
}

function Publish-BridgeSeat {
    param(
        [string] $BridgeRoot,
        [string] $FallbackConfig
    )
    # aid-publish replaces the target tree — stop bridge holding DLL locks.
    Get-Process -Name CdpMcpBridge, CdpMcp -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $path = $_.MainModule.FileName
            if ($path -and ($path.StartsWith($BridgeRoot, [StringComparison]::OrdinalIgnoreCase))) {
                Write-Host "Stopping lock holder: $($_.ProcessName) ($path)"
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            }
        } catch { }
    }
    Start-Sleep -Milliseconds 500

    New-Item -ItemType Directory -Force -Path $BridgeRoot | Out-Null
    Publish-CdpProject -Project $bridgeCsproj -DeployRoot $BridgeRoot
    Ensure-Config -DeployRoot $BridgeRoot -FallbackConfig $FallbackConfig
}

function Stop-LockHoldersUnder {
    param([string] $Root)
    if (-not $Root) { return }
    Get-Process CdpService, CdpMcp, CdpMcpBridge -ErrorAction SilentlyContinue | ForEach-Object {
        try {
            $path = $_.MainModule.FileName
            if ($path -and ($path.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase))) {
                Write-Host "Stopping lock holder: $($_.ProcessName) ($path)"
                Stop-Process -Id $_.Id -Force -ErrorAction SilentlyContinue
            }
        } catch { }
    }
    Start-Sleep -Milliseconds 800
}

function Promote-StagedTree {
    param(
        [string] $StagedRoot,
        [string] $LiveRoot,
        [string[]] $PreserveNames = @("cdp-mcp.toml")
    )
    if (-not (Test-Path -LiteralPath $StagedRoot)) {
        Write-Error "Staged tree missing: $StagedRoot"
        exit 1
    }
    New-Item -ItemType Directory -Force -Path $LiveRoot | Out-Null

    $backups = @{}
    foreach ($name in $PreserveNames) {
        $livePath = Join-Path $LiveRoot $name
        if (Test-Path -LiteralPath $livePath) {
            $backups[$name] = Get-Content -LiteralPath $livePath -Raw -ErrorAction SilentlyContinue
        }
    }

    $robocopy = Get-Command robocopy -ErrorAction SilentlyContinue
    if ($robocopy) {
        & robocopy $StagedRoot $LiveRoot /MIR /NFL /NDL /NJH /NJS /NC /NS /NP | Out-Null
        $rc = $LASTEXITCODE
        if ($rc -ge 8) {
            Write-Error "robocopy promote failed exit=$rc ($StagedRoot -> $LiveRoot)"
            exit $rc
        }
    } else {
        Write-Warning "robocopy missing — Copy-Item fallback (slower)."
        Get-ChildItem -LiteralPath $LiveRoot -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Copy-Item -LiteralPath (Join-Path $StagedRoot '*') -Destination $LiveRoot -Recurse -Force
    }

    foreach ($name in $backups.Keys) {
        if ($null -ne $backups[$name]) {
            Set-Content -LiteralPath (Join-Path $LiveRoot $name) -Value $backups[$name] -Encoding utf8
            Write-Host "Preserve: kept live config $name"
        }
    }
}

function Resolve-LiveFromStaged {
    param([string] $StagedRoot, [string] $DefaultLive)
    if (-not $StagedRoot) { return $DefaultLive }
    if ($StagedRoot.EndsWith(".next", [StringComparison]::OrdinalIgnoreCase)) {
        return $StagedRoot.Substring(0, $StagedRoot.Length - 5)
    }
    return $DefaultLive
}

function Apply-PendingUpdate {
    param(
        [string] $ServiceTarget,
        [string] $BridgeTarget,
        [string] $BridgeDebugTarget,
        [switch] $NoNudgeMcp,
        [switch] $NudgeAllSeats
    )
    $pendingMarker = Join-Path $ServiceTarget "cdp-pending-update.json"
    if (-not (Test-Path -LiteralPath $pendingMarker)) {
        Write-Error "No pending update at $pendingMarker — run -Mode soft first."
        exit 1
    }

    $pending = Get-Content -LiteralPath $pendingMarker -Raw | ConvertFrom-Json
    $serviceNext = [string]$pending.service_root
    if (-not $serviceNext) { $serviceNext = "$ServiceTarget.next" }

    Write-Host "APPLY pending staged_at=$($pending.staged_at_utc)"
    Write-Host "  service: $serviceNext -> $ServiceTarget"

    Stop-LockHoldersUnder -Root $ServiceTarget
    Stop-LockHoldersUnder -Root $BridgeTarget
    if ($BridgeDebugTarget -and ($BridgeDebugTarget -ne $BridgeTarget)) {
        Stop-LockHoldersUnder -Root $BridgeDebugTarget
    }

    Promote-StagedTree -StagedRoot $serviceNext -LiveRoot $ServiceTarget

    $bridgePromotes = @(
        @{ Staged = [string]$pending.bridge_root; Live = $BridgeTarget },
        @{ Staged = "$BridgeTarget.next"; Live = $BridgeTarget },
        @{ Staged = "$BridgeDebugTarget.next"; Live = $BridgeDebugTarget }
    )
    $seen = @{}
    foreach ($bp in $bridgePromotes) {
        $staged = $bp.Staged
        if (-not $staged -or $seen.ContainsKey($staged.ToLowerInvariant())) { continue }
        if (-not (Test-Path -LiteralPath $staged)) { continue }
        $live = Resolve-LiveFromStaged -StagedRoot $staged -DefaultLive $bp.Live
        if ($live -and ($live -ne $ServiceTarget)) {
            Write-Host "  bridge: $staged -> $live"
            Promote-StagedTree -StagedRoot $staged -LiveRoot $live
            $seen[$staged.ToLowerInvariant()] = $true
        }
    }

    Remove-Item -LiteralPath $pendingMarker -Force -ErrorAction SilentlyContinue
    foreach ($nextDir in @("$ServiceTarget.next", "$BridgeTarget.next", "$BridgeDebugTarget.next")) {
        if (Test-Path -LiteralPath $nextDir) {
            Remove-Item -LiteralPath $nextDir -Recurse -Force -ErrorAction SilentlyContinue
            Write-Host "Cleaned staged: $nextDir"
        }
    }

    $serviceConfig = Join-Path $ServiceTarget "cdp-mcp.toml"
    & (Join-Path $here "Start-CdpService.ps1") -Target $ServiceTarget -Config $serviceConfig

    if (-not $NoNudgeMcp) {
        try {
            $nudge = Invoke-CdpBridgeDeployNudge -BridgeTarget $BridgeTarget -BridgeDebugTarget $BridgeDebugTarget -NudgeAllSeats:$NudgeAllSeats
            if ($nudge.Ok) {
                $seatList = ($nudge.Servers -join ', ')
                Write-Host "MCP nudge (bridge): $($nudge.Path) CDP_RELOAD_NUDGE=$($nudge.Value) seats=$seatList"
            } else {
                Write-Host "MCP nudge skipped: $($nudge.Error)"
            }
        } catch {
            Write-Host "MCP nudge failed: $($_.Exception.Message)"
        }
    }

    $serviceExe = Join-Path $ServiceTarget "CdpService.exe"
    Write-Host "APPLY ok service: $serviceExe"
}

$serviceDeployRoot = if ($Mode -eq "soft") { "$ServiceTarget.next" } else { $ServiceTarget }
$bridgeDeployRoot = if ($Mode -eq "soft") { "$BridgeTarget.next" } else { $BridgeTarget }
$pendingMarker = Join-Path $ServiceTarget "cdp-pending-update.json"
$liveBridgeConfig = Join-Path $BridgeTarget "cdp-mcp.toml"
$liveBridgeDebugConfig = Join-Path $BridgeDebugTarget "cdp-mcp.toml"

Push-Location $here
try {
    if ($Mode -eq "apply") {
        Apply-PendingUpdate -ServiceTarget $ServiceTarget -BridgeTarget $BridgeTarget `
            -BridgeDebugTarget $BridgeDebugTarget -NoNudgeMcp:$NoNudgeMcp -NudgeAllSeats:$NudgeAllSeats
        exit $LASTEXITCODE
    }

    Write-Host "Publish CdpService → $serviceDeployRoot"
    Publish-CdpProject -Project $serviceCsproj -DeployRoot $serviceDeployRoot -KillRunning:($Mode -eq "hard")
    Copy-TsWorker -DeployRoot $serviceDeployRoot
    $serviceExeSrc = Join-Path $serviceDeployRoot "CdpMcp.exe"
    $serviceExeDst = Join-Path $serviceDeployRoot "CdpService.exe"
    if (Test-Path -LiteralPath $serviceExeSrc) {
        Copy-Item -LiteralPath $serviceExeSrc -Destination $serviceExeDst -Force
    }
    $serviceConfigDst = Ensure-Config -DeployRoot $serviceDeployRoot

    Write-Host "Publish CdpMcpBridge → $bridgeDeployRoot"
    Publish-BridgeSeat -BridgeRoot $bridgeDeployRoot -FallbackConfig $serviceConfigDst

    if ($BridgeDebugTarget -and $BridgeDebugTarget -ne $BridgeTarget) {
        Write-Host "Publish CdpMcpBridge → $BridgeDebugTarget"
        $debugDeployRoot = if ($Mode -eq "soft") { "$BridgeDebugTarget.next" } else { $BridgeDebugTarget }
        Publish-BridgeSeat -BridgeRoot $debugDeployRoot -FallbackConfig $serviceConfigDst
    }

    $bridgeExe = Join-Path $bridgeDeployRoot "CdpMcpBridge.exe"
    $liveBridgeExe = Join-Path $BridgeTarget "CdpMcpBridge.exe"
    $bridgeExeJson = $liveBridgeExe.Replace('\', '\\')
    $bridgeConfigJson = $liveBridgeConfig.Replace('\', '/')

    if ($Mode -eq "soft") {
        New-Item -ItemType Directory -Force -Path $ServiceTarget | Out-Null
        $ver = $null
        try { $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($serviceExeDst).FileVersion } catch { }
        $pending = [ordered]@{
            schema        = "cdp_pending_update/v0"
            mode          = "soft"
            staged_at_utc = (Get-Date).ToUniversalTime().ToString("o")
            service_root  = $serviceDeployRoot
            bridge_root   = $bridgeDeployRoot
            version       = $ver
            apply_hint    = "cdp_deploy mode=apply (promote .next, no republish) or .\publish-and-deploy.ps1 -Mode apply"
        }
        ($pending | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $pendingMarker -Encoding utf8
        Write-Host "SOFT staged service: $serviceDeployRoot"
        Write-Host "SOFT staged bridge:  $bridgeDeployRoot"
        Write-Host "Pending: $pendingMarker"
    } else {
        if (Test-Path -LiteralPath $pendingMarker) { Remove-Item -LiteralPath $pendingMarker -Force }
        Write-Host "HARD deployed service: $serviceExeDst"
        Write-Host "HARD deployed bridge:  $bridgeExe"

        & (Join-Path $here "Start-CdpService.ps1") -Target $ServiceTarget -Config $serviceConfigDst

        if (-not $NoNudgeMcp) {
            try {
                $nudge = Invoke-CdpBridgeDeployNudge -BridgeTarget $BridgeTarget -BridgeDebugTarget $BridgeDebugTarget -NudgeAllSeats:$NudgeAllSeats
                if ($nudge.Ok) {
                    $seatList = ($nudge.Servers -join ', ')
                    Write-Host "MCP nudge (bridge): $($nudge.Path) CDP_RELOAD_NUDGE=$($nudge.Value) seats=$seatList"
                } else {
                    Write-Host "MCP nudge skipped: $($nudge.Error)"
                }
            } catch {
                Write-Host "MCP nudge failed: $($_.Exception.Message)"
            }
        }
    }

    Write-Host ""
    Write-Host "Cursor MCP (bridge — ADR-0198):"
    Write-Host @"
  "cdp": {
    "command": "$bridgeExeJson",
    "args": ["--config", "$bridgeConfigJson"],
    "env": { "CDP_MCP_TOOLSET": "0.4.0-session" }
  }
"@
} finally {
    Pop-Location
}
