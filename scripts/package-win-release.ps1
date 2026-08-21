<#
.SYNOPSIS
  Publish self-contained CdpMcp for a RID and zip it for GitHub Releases.
  No KillRunning. Does not touch live D:\cdp-mcp.

.PARAMETER Runtime
  win-x64 | linux-x64 | osx-x64 | osx-arm64
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [ValidateSet("win-x64", "linux-x64", "osx-x64", "osx-arm64")]
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
Remove-Item Env:MSBUILD_EXE_PATH, Env:MSBuildExtensionsPath, Env:MSBuildSDKsPath -ErrorAction SilentlyContinue

$here = Split-Path -Parent $PSScriptRoot
$open = Split-Path -Parent $here
$csproj = Join-Path $here "CdpMcp.csproj"
if (-not (Test-Path -LiteralPath $csproj)) { throw "CdpMcp.csproj not found: $csproj" }

$ver = ([regex]::Match((Get-Content -LiteralPath $csproj -Raw), '<Version>([^<]+)</Version>')).Groups[1].Value
if ([string]::IsNullOrWhiteSpace($ver)) { $ver = "0.0.0" }

$publishDir = Join-Path (Join-Path (Join-Path $here "artifacts") "publish") $Runtime
$distDir = Join-Path (Join-Path $here "artifacts") "dist"
$zipName = "CdpMcp-$ver-$Runtime.zip"
$zipPath = Join-Path $distDir $zipName
$sumsPath = Join-Path $distDir "SHA256SUMS-$Runtime"

Write-Host "Publish CdpMcp $ver $Runtime → $publishDir"
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir, $distDir | Out-Null

& dotnet publish $csproj -c $Configuration -r $Runtime --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$binName = if ($Runtime -like "win-*") { "CdpMcp.exe" } else { "CdpMcp" }
$binPath = Join-Path $publishDir $binName
if (-not (Test-Path -LiteralPath $binPath)) {
    throw "Publish output missing $binName under $publishDir"
}
if ($Runtime -notlike "win-*") {
    & chmod +x $binPath
    if ($LASTEXITCODE -ne 0) { Write-Host "WARN: chmod +x $binPath failed" -ForegroundColor Yellow }
}

$workerSrc = Join-Path (Join-Path $open "typescript-lang") "worker"
$workerDst = Join-Path $publishDir "ts-worker"
if (Test-Path -LiteralPath (Join-Path $workerSrc "index.mjs")) {
    if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $workerSrc "node_modules") "typescript"))) {
        Push-Location $workerSrc
        try { & npm install --omit=dev; if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE } }
        finally { Pop-Location }
    }
    if (Test-Path -LiteralPath $workerDst) { Remove-Item $workerDst -Recurse -Force }
    Copy-Item $workerSrc $workerDst -Recurse -Force
}
else {
    Write-Host "WARN: typescript-lang worker missing — TS facet will be empty in this zip" -ForegroundColor Yellow
}

Get-ChildItem -LiteralPath $publishDir -Filter "cdp-mcp.toml" -Recurse -File -ErrorAction SilentlyContinue |
    ForEach-Object { Remove-Item -LiteralPath $_.FullName -Force }

$installSrc = Join-Path $PSScriptRoot "Install-Cdp.ps1"
if (Test-Path -LiteralPath $installSrc) {
    Copy-Item $installSrc (Join-Path $distDir "Install-Cdp.ps1") -Force
}
$installSh = Join-Path $PSScriptRoot "install-cdp.sh"
if (Test-Path -LiteralPath $installSh) {
    Copy-Item $installSh (Join-Path $distDir "install-cdp.sh") -Force
}
$tomlSrc = Join-Path $PSScriptRoot "cdp-mcp.toml.example"
if (Test-Path -LiteralPath $tomlSrc) {
    Copy-Item $tomlSrc (Join-Path $distDir "cdp-mcp.toml.example") -Force
}

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

function New-CdpReleaseZip([string]$SourceDir, [string]$OutZip) {
    $isWin = if ($null -ne (Get-Variable IsWindows -Scope Global -ErrorAction SilentlyContinue)) { [bool]$IsWindows } else { $true }
    if ($isWin -and (Get-Command tar.exe -ErrorAction SilentlyContinue)) {
        Push-Location $SourceDir
        try {
            & tar.exe -a -c -f $OutZip *
            if ($LASTEXITCODE -ne 0) { throw "tar zip failed: $OutZip" }
        }
        finally { Pop-Location }
        return
    }
    # Linux/macOS runners: Compress-Archive (pwsh) — portable zip
    $items = @(Get-ChildItem -LiteralPath $SourceDir -Force | ForEach-Object { $_.FullName })
    if ($items.Count -eq 0) { throw "Nothing to zip under $SourceDir" }
    Compress-Archive -Path $items -DestinationPath $OutZip -Force
}

New-CdpReleaseZip $publishDir $zipPath

$sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $sumsPath -Value "$sha  $zipName" -Encoding ascii
# Also append/merge into SHA256SUMS for single-artifact convenience when one RID
$merged = Join-Path $distDir "SHA256SUMS"
if (Test-Path -LiteralPath $merged) {
    $existing = Get-Content -LiteralPath $merged -ErrorAction SilentlyContinue
    $rest = @($existing | Where-Object { $_ -notmatch [regex]::Escape($zipName) + '$' })
    Set-Content -LiteralPath $merged -Value (@($rest) + "$sha  $zipName") -Encoding ascii
}
else {
    Set-Content -LiteralPath $merged -Value "$sha  $zipName" -Encoding ascii
}
Write-Host "OK $zipPath"
Write-Host "SHA256 $sha"
