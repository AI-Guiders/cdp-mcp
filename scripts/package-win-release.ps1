<#
.SYNOPSIS
  Publish self-contained win-x64 CdpMcp and zip it for GitHub Releases.
  No KillRunning. Does not touch live D:\cdp-mcp.
#>
[CmdletBinding()]
param(
    [string]$Configuration = "Release",
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

$publishDir = Join-Path $here "artifacts\publish\$Runtime"
$distDir = Join-Path $here "artifacts\dist"
$zipName = "CdpMcp-$ver-$Runtime.zip"
$zipPath = Join-Path $distDir $zipName

Write-Host "Publish CdpMcp $ver $Runtime → $publishDir"
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir, $distDir | Out-Null

& dotnet publish $csproj -c $Configuration -r $Runtime --self-contained true -o $publishDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$workerSrc = Join-Path $open "typescript-lang\worker"
$workerDst = Join-Path $publishDir "ts-worker"
if (Test-Path -LiteralPath (Join-Path $workerSrc "index.mjs")) {
    if (-not (Test-Path -LiteralPath (Join-Path $workerSrc "node_modules\typescript"))) {
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
$tomlSrc = Join-Path $PSScriptRoot "cdp-mcp.toml.example"
if (Test-Path -LiteralPath $tomlSrc) {
    Copy-Item $tomlSrc (Join-Path $distDir "cdp-mcp.toml.example") -Force
}

if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Push-Location $publishDir
try {
    & tar.exe -a -c -f $zipPath *
    if ($LASTEXITCODE -ne 0) { throw "tar zip failed: $zipPath" }
}
finally { Pop-Location }

$sha = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath (Join-Path $distDir "SHA256SUMS") -Value "$sha  $zipName" -Encoding ascii
Write-Host "OK $zipPath"
Write-Host "SHA256 $sha"
