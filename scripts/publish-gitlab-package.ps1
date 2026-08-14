<#
.SYNOPSIS
  Upload artifacts/dist to GitLab Generic Package (same gesture as agent-notes-mcp).

.DESCRIPTION
  Does not build. Run package-win-release.ps1 first.
  Env: GITLAB_URL (default http://193.124.113.7), GITLAB_TOKEN (api).
  In GitLab CI: CI_SERVER_URL + CI_JOB_TOKEN + CI_PROJECT_ID.
#>
[CmdletBinding()]
param(
    [string]$Version = "",
    [string]$Tag = "",
    [string]$GitLabUrl = "",
    [string]$Token = "",
    [string]$ProjectPath = "Krawler/financial-open",
    [string]$PackageName = "cdp-mcp",
    [string]$DistDir = "",
    [switch]$CreateRelease
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $PSScriptRoot
$csproj = Join-Path $here "CdpMcp.csproj"
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = ([regex]::Match((Get-Content -LiteralPath $csproj -Raw), '<Version>([^<]+)</Version>')).Groups[1].Value
}
if ([string]::IsNullOrWhiteSpace($Version)) { throw "Version missing — pass -Version or set CdpMcp.csproj <Version>." }
if ([string]::IsNullOrWhiteSpace($Tag)) { $Tag = "v$Version" }

$baseUrl = if ($GitLabUrl) { $GitLabUrl.TrimEnd('/') }
    elseif ($env:GITLAB_URL) { $env:GITLAB_URL.TrimEnd('/') }
    elseif ($env:CI_SERVER_URL) { $env:CI_SERVER_URL.TrimEnd('/') }
    else { "http://193.124.113.7" }

$token = if ($Token) { $Token } elseif ($env:GITLAB_TOKEN) { $env:GITLAB_TOKEN } else { "" }
$jobToken = $env:CI_JOB_TOKEN
if (-not $token -and -not $jobToken) { throw "Set GITLAB_TOKEN (or -Token), or run in GitLab CI with CI_JOB_TOKEN." }

$headers = @{ "User-Agent" = "publish-gitlab-package" }
if ($token) { $headers["PRIVATE-TOKEN"] = $token } else { $headers["JOB-TOKEN"] = $jobToken }

$projectId = if ($env:CI_PROJECT_ID) { $env:CI_PROJECT_ID } else { $ProjectPath -replace '/', '%2F' }
$api = "$baseUrl/api/v4"
if ([string]::IsNullOrWhiteSpace($DistDir)) { $DistDir = Join-Path $here "artifacts\dist" }
if (-not (Test-Path -LiteralPath $DistDir)) { throw "Dist missing: $DistDir — run package-win-release.ps1 first." }

$files = @(
    "CdpMcp-$Version-win-x64.zip",
    "Install-Cdp.ps1",
    "cdp-mcp.toml.example",
    "SHA256SUMS"
) | ForEach-Object {
    $p = Join-Path $DistDir $_
    if (-not (Test-Path -LiteralPath $p)) { throw "Missing dist file: $p" }
    @{ Name = $_; Path = $p }
}

foreach ($z in $files) {
    $uploadUrl = "$api/projects/$projectId/packages/generic/$PackageName/$Version/$($z.Name)"
    Write-Host "Uploading $($z.Name) → $PackageName/$Version"
    Invoke-RestMethod -Uri $uploadUrl -Method Put -InFile $z.Path -Headers $headers -ContentType "application/octet-stream" | Out-Null
}

if ($CreateRelease) {
    $commitSha = (git -C $here rev-parse HEAD).Trim()
    $body = (@{
        tag_name    = $Tag
        ref         = $commitSha
        name        = "CDP $Tag"
        description = "win-x64 Generic Package $PackageName/$Version"
    } | ConvertTo-Json)
    try {
        Invoke-RestMethod -Uri "$api/projects/$projectId/releases" -Method Post -Headers $headers -Body $body -ContentType "application/json" | Out-Null
        Write-Host "Release $Tag created."
    }
    catch {
        Write-Warning "Release ${Tag}: $_"
    }
    foreach ($z in $files) {
        $assetUrl = "$api/projects/$projectId/packages/generic/$PackageName/$Version/$($z.Name)"
        $linkBody = (@{ name = $z.Name; url = $assetUrl; link_type = "package" } | ConvertTo-Json)
        try {
            Invoke-RestMethod -Uri "$api/projects/$projectId/releases/$Tag/assets/links" -Method Post -Headers $headers -Body $linkBody -ContentType "application/json; charset=utf-8" | Out-Null
        }
        catch {
            Write-Warning "Asset link $($z.Name): $_"
        }
    }
}

Write-Host "OK GitLab $baseUrl $PackageName/$Version"
