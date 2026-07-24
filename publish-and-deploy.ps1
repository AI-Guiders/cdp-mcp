# Publish Release (win-x64, self-contained) and mirror for Cursor MCP.
# Run from an EXTERNAL terminal (Cursor Shell / WT) — not via cdp_shell_*:
# hard mode KillRunning cannot terminate the MCP host from inside its own process tree.
#
# Modes:
#   soft (default) — stage to <Target>.next WITHOUT killing live MCP; write
#                    <Target>\cdp-pending-update.json so cdp_health.runtime.pending_update shows it.
#   hard           — KillRunning + deploy to <Target>; clear pending marker. Then Reload MCP.
#
# Run:  cd ...\cdp-mcp  ;  .\publish-and-deploy.ps1
#       .\publish-and-deploy.ps1 -Mode hard
#       .\publish-and-deploy.ps1 -UseNuGet   # force AIGuiders.Cdp.* packages even if sibling csproj exist
# Optional: -Target "D:\cdp-mcp" (release) or "D:\cdp-mcp-debug" (experimental spare).
# Existing <Target>\cdp-mcp.toml is kept (template seeds only when missing).
[CmdletBinding()]
param(
    [string] $Target = "D:\cdp-mcp",
    [ValidateSet("soft", "hard")]
    [string] $Mode = "soft",
    # Prefer NuGet for Cdp.Core / Cdp.ScriptableIde (ignore sibling ProjectReference).
    # Other backends still need the monorepo until they have package fallbacks.
    [switch] $UseNuGet
)

$ErrorActionPreference = "Stop"
$here = $PSScriptRoot
$csproj = Join-Path $here "CdpMcp.csproj"
if (-not (Test-Path -LiteralPath $csproj)) {
    Write-Error "CdpMcp.csproj not found under $here"
    exit 1
}

$deployRoot = if ($Mode -eq "soft") { "$Target.next" } else { $Target }
$pendingMarker = Join-Path $Target "cdp-pending-update.json"

Push-Location $here
try {
    $publishArgs = @(
        "-Project", $csproj,
        "-Target", $deployRoot,
        "-Runtime", "win-x64",
        "-Configuration", "Release",
        "-SelfContained"
    )
    if ($Mode -eq "hard") {
        $publishArgs += "-KillRunning"
    }
    if ($UseNuGet) {
        Write-Host "aid-publish -UseNuGet (/p:AidUseNuGet=true)"
        $publishArgs += "-UseNuGet"
    }

    if (Test-Path -LiteralPath (Join-Path $here ".config\dotnet-tools.json")) {
        & dotnet tool restore | Out-Null
        & dotnet aid-publish @publishArgs
    } else {
        & aid-publish @publishArgs
    }
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    $configSrc = Join-Path $here "config\cdp-mcp.toml"
    if (-not (Test-Path -LiteralPath $configSrc)) {
        Write-Error "Missing config template: $configSrc"
        exit 1
    }
    $configDst = Join-Path $deployRoot "cdp-mcp.toml"
    if (Test-Path -LiteralPath $configDst) {
        Write-Host "Keep existing config: $configDst (template not applied)"
    } else {
        Copy-Item -LiteralPath $configSrc -Destination $configDst -Force
        Write-Host "Seeded config from template: $configDst"
    }

    # TypeScript LanguageService worker (Node) — side-by-side with CdpMcp.exe
    $workerSrc = Join-Path $here "..\typescript-lang\worker"
    $workerDst = Join-Path $deployRoot "ts-worker"
    if (-not (Test-Path -LiteralPath (Join-Path $workerSrc "index.mjs"))) {
        Write-Error "Missing TS worker: $workerSrc\index.mjs"
        exit 1
    }
    if (-not (Test-Path -LiteralPath (Join-Path $workerSrc "node_modules\typescript"))) {
        Write-Host "npm install in ts-worker source..."
        Push-Location $workerSrc
        try {
            & npm install --omit=dev
            if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
        } finally {
            Pop-Location
        }
    }
    if (Test-Path -LiteralPath $workerDst) {
        Remove-Item -LiteralPath $workerDst -Recurse -Force
    }
    Copy-Item -LiteralPath $workerSrc -Destination $workerDst -Recurse -Force

    $exe = Join-Path $deployRoot "CdpMcp.exe"
    $liveExe = Join-Path $Target "CdpMcp.exe"
    $exeJson = $liveExe.Replace('\', '\\')
    $liveConfig = Join-Path $Target "cdp-mcp.toml"
    $configJson = $liveConfig.Replace('\', '/')

    if ($Mode -eq "soft") {
        New-Item -ItemType Directory -Force -Path $Target | Out-Null
        $ver = $null
        try {
            $ver = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
        } catch { }
        $pending = [ordered]@{
            schema         = "cdp_pending_update/v0"
            mode           = "soft"
            staged_at_utc  = (Get-Date).ToUniversalTime().ToString("o")
            staged_root    = $deployRoot
            staged_exe     = $exe
            version        = $ver
            apply_hint     = "From external terminal: .\publish-and-deploy.ps1 -Mode hard  (or copy $deployRoot → $Target), then Reload CDP MCP in Cursor."
        }
        ($pending | ConvertTo-Json -Depth 5) | Set-Content -LiteralPath $pendingMarker -Encoding utf8
        Write-Host ""
        Write-Host "SOFT staged: $exe"
        Write-Host "Pending:    $pendingMarker"
        Write-Host "Live still: $liveExe (Reload not required yet; cdp_health.runtime.pending_update shows stage)"
        Write-Host "Apply:      .\publish-and-deploy.ps1 -Mode hard"
    } else {
        if (Test-Path -LiteralPath $pendingMarker) {
            Remove-Item -LiteralPath $pendingMarker -Force
        }
        Write-Host ""
        Write-Host "HARD deployed: $exe"
        Write-Host "Config:        $configDst"
        Write-Host "Pending cleared. Toggle/reload MCP in Cursor if disconnected."
    }

    Write-Host ""
    Write-Host "Cursor MCP (live path):"
    Write-Host @"
  "cdp": {
    "command": "$exeJson",
    "args": ["--config", "$configJson"],
    "env": { "CDP_MCP_TOOLSET": "0.4.0-session" }
  }
"@
} finally {
    Pop-Location
}

