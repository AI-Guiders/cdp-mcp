<#
.SYNOPSIS
  Install CDP from a GitLab Generic Package zip (no clone, no build).

.DESCRIPTION
  Default: download CdpMcp-*-win-x64.zip from Krawler/financial-open package cdp-mcp,
  seed kb-public + empty personal canon, merge host MCP json.
  -CdpSource is an escape for a local published folder (maintainers).
  Private GitLab: set GITLAB_TOKEN (api read) or -GitLabToken.
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path $env:LOCALAPPDATA "AIGuiders"),
    [string]$CdpSource = "",
    [string]$GitLabUrl = $(if ($env:GITLAB_URL) { $env:GITLAB_URL } else { "http://193.124.113.7" }),
    [string]$GitLabProject = $(if ($env:CDP_GITLAB_PROJECT) { $env:CDP_GITLAB_PROJECT } else { "Krawler/financial-open" }),
    [string]$PackageName = "cdp-mcp",
    [string]$ReleaseTag = "latest",
    [string]$GitLabToken = "",
    [string]$KbPublicRepo = "https://github.com/AI-Guiders/kb-public.git",
    [ValidateSet("cursor", "claude", "vscode", "none")]
    [string]$HostAdapter = "cursor",
    [switch]$Upgrade,
    [switch]$SkipKbClone,
    [switch]$ForceDownload,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding $false

function Write-Utf8File([string]$Path, [string]$Text) {
    if ($WhatIf) { Write-Host "WhatIf: write $Path"; return }
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    [System.IO.File]::WriteAllText($Path, $Text.Replace("`r`n", "`n").Replace("`n", "`r`n"), $utf8)
}

function Convert-ToTomlPath([string]$Path) { return ($Path -replace "\\", "/") }

function Get-GitlabHeaders {
    $h = @{ "User-Agent" = "Install-Cdp" }
    $t = $GitLabToken
    if ([string]::IsNullOrWhiteSpace($t)) { $t = $env:GITLAB_TOKEN }
    if (-not [string]::IsNullOrWhiteSpace($t)) { $h["PRIVATE-TOKEN"] = $t }
    return $h
}

function Get-GitlabProjectId([string]$Path) {
    return [uri]::EscapeDataString($Path)
}

function Get-GitlabGenericUrl([string]$Version, [string]$FileName) {
    $base = $GitLabUrl.TrimEnd('/')
    $id = Get-GitlabProjectId $GitLabProject
    return "$base/api/v4/projects/$id/packages/generic/$PackageName/$Version/$FileName"
}

function Resolve-CdpPackageVersion {
    $tag = $ReleaseTag.Trim()
    if ($tag -ne "latest") { return $tag.TrimStart('v') }
    $base = $GitLabUrl.TrimEnd('/')
    $id = Get-GitlabProjectId $GitLabProject
    $api = "$base/api/v4/projects/$id/packages?package_name=$PackageName&package_type=generic&sort=desc&per_page=5"
    Write-Host "GitLab packages $GitLabProject / $PackageName"
    if ($WhatIf) { return "0.0.0-whatif" }
    $pkgs = Invoke-RestMethod -Uri $api -Headers (Get-GitlabHeaders)
    $hit = @($pkgs) | Select-Object -First 1
    if (-not $hit) {
        throw "No generic package '$PackageName' in $GitLabProject. Publish with publish-gitlab-package.ps1 or pass -ReleaseTag 0.5.715. Private project needs GITLAB_TOKEN."
    }
    return [string]$hit.version
}

function Get-CdpGitlabFile([string]$Version, [string]$FileName, [string]$OutFile) {
    $url = Get-GitlabGenericUrl $Version $FileName
    Write-Host "Download $FileName"
    Invoke-WebRequest -Uri $url -OutFile $OutFile -Headers (Get-GitlabHeaders)
}

function Get-CdpGitlabPayload {
    $ver = Resolve-CdpPackageVersion
    $zipName = "CdpMcp-$ver-win-x64.zip"
    if ($WhatIf) {
        Write-Host "WhatIf: download $zipName from $GitLabProject"
        return Join-Path $env:TEMP "cdp-payload-whatif"
    }
    $zip = Join-Path $env:TEMP $zipName
    Get-CdpGitlabFile $ver $zipName $zip
    $dest = Join-Path $env:TEMP ("cdp-payload-" + $ver)
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $dest -Force
    $exeHit = Get-ChildItem -LiteralPath $dest -Filter CdpMcp.exe -Recurse -File | Select-Object -First 1
    if (-not $exeHit) { throw "Zip had no CdpMcp.exe: $zip" }
    return $exeHit.DirectoryName
}

function Resolve-CdpSourceFolder {
    if (-not [string]::IsNullOrWhiteSpace($CdpSource)) {
        return (Resolve-Path -LiteralPath $CdpSource).Path
    }
    $existing = Join-Path $Root "cdp\CdpMcp.exe"
    if (-not $ForceDownload -and (Test-Path -LiteralPath $existing)) {
        return (Split-Path -Parent $existing)
    }
    return Get-CdpGitlabPayload
}

function Merge-McpServers([string]$TargetPath, [string]$Command, [string[]]$Args) {
    if ($WhatIf) { Write-Host "WhatIf: merge cdp into $TargetPath"; return }
    $dir = Split-Path -Parent $TargetPath
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    if (Test-Path -LiteralPath $TargetPath) {
        $mcp = Get-Content -LiteralPath $TargetPath -Raw -Encoding UTF8 | ConvertFrom-Json
    }
    else {
        $mcp = [pscustomobject]@{ mcpServers = [pscustomobject]@{} }
    }
    if ($null -eq $mcp.mcpServers) {
        $mcp | Add-Member -NotePropertyName mcpServers -NotePropertyValue ([pscustomobject]@{}) -Force
    }
    $servers = [ordered]@{}
    if ($mcp.mcpServers) {
        $mcp.mcpServers.PSObject.Properties | ForEach-Object { $servers[$_.Name] = $_.Value }
    }
    $servers["cdp"] = [pscustomobject]@{ command = $Command; args = @($Args) }
    $mcp.mcpServers = [pscustomobject]$servers
    Write-Utf8File $TargetPath ($mcp | ConvertTo-Json -Depth 12)
    Write-Host "Merged mcp key 'cdp' → $TargetPath"
}

$templateToml = Join-Path $PSScriptRoot "cdp-mcp.toml.example"
if (-not (Test-Path -LiteralPath $templateToml)) {
    $fallback = Join-Path $env:TEMP "cdp-mcp.toml.example"
    Write-Host "Fetch toml example from GitLab package"
    if (-not $WhatIf) {
        $ver = Resolve-CdpPackageVersion
        Get-CdpGitlabFile $ver "cdp-mcp.toml.example" $fallback
    }
    $templateToml = $fallback
}

$cdpSrc = Resolve-CdpSourceFolder
$cdpDst = Join-Path $Root "cdp"
$kbDst = Join-Path $Root "kb-public"
$notesDst = Join-Path $Root "agent-notes"
$taskDst = Join-Path $Root "task-knowledge"
$notesToml = Join-Path $cdpDst "agent-notes-mcp.toml"
$taskToml = Join-Path $cdpDst "agent-task-knowledge-mcp.toml"
$cdpToml = Join-Path $cdpDst "cdp-mcp.toml"
$exe = Join-Path $cdpDst "CdpMcp.exe"

Write-Host "Install CDP → $Root" -ForegroundColor Cyan
Write-Host "  source: $cdpSrc"
Write-Host "  host:   $HostAdapter"
Write-Host "  gitlab: $GitLabUrl  $GitLabProject"

if ($WhatIf) {
    Write-Host "WhatIf: copy payload $cdpSrc → $cdpDst"
}
else {
    New-Item -ItemType Directory -Force -Path $cdpDst, $notesDst, $taskDst | Out-Null
    $preserveToml = @{}
    if ($Upgrade) {
        Get-ChildItem -LiteralPath $cdpDst -Filter "*.toml" -File -ErrorAction SilentlyContinue | ForEach-Object {
            $preserveToml[$_.Name] = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
        }
    }
    & robocopy.exe $cdpSrc $cdpDst /E /XD ts-worker.node_modules /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "robocopy payload failed ($LASTEXITCODE)" }
    foreach ($kv in $preserveToml.GetEnumerator()) {
        [System.IO.File]::WriteAllText((Join-Path $cdpDst $kv.Key), $kv.Value, $utf8)
    }
}

if (-not $SkipKbClone) {
    if (Test-Path -LiteralPath (Join-Path $kbDst ".git")) {
        Write-Host "kb-public exists — git pull"
        if (-not $WhatIf) {
            git -C $kbDst pull --ff-only
            if ($LASTEXITCODE -ne 0) { throw "git pull kb-public failed" }
        }
    }
    else {
        Write-Host "Clone kb-public → $kbDst"
        if (-not $WhatIf) {
            git clone --depth 1 $KbPublicRepo $kbDst
            if ($LASTEXITCODE -ne 0) { throw "git clone kb-public failed" }
        }
    }
}

$personalMarker = Join-Path $notesDst "agent-notes.md"
if (-not (Test-Path -LiteralPath $personalMarker)) {
    Write-Host "Seed personal canon from kb-public templates/newcomer"
    $newcomer = Join-Path $kbDst "knowledge\templates\newcomer"
    $metaSrc = Join-Path $kbDst "knowledge\META"
    $localDir = Join-Path $notesDst "knowledge\work\local"
    if (-not $WhatIf) {
        New-Item -ItemType Directory -Force -Path $localDir | Out-Null
        foreach ($pair in @(
                @{ S = "template-clean-setup-workspace-scope-map-v1.md"; D = "workspace-scope-map-v1.md" },
                @{ S = "template-clean-setup-knowledge-roots-index-v1.md"; D = "knowledge-roots-index-v1.md" }
            )) {
            $s = Join-Path $newcomer $pair.S
            if (Test-Path -LiteralPath $s) { Copy-Item $s (Join-Path $localDir $pair.D) -Force }
        }
        if (Test-Path -LiteralPath $metaSrc) {
            Copy-Item $metaSrc (Join-Path $notesDst "knowledge\META") -Recurse -Force
        }
    }
    $hot = @()
    foreach ($n in @("template-clean-setup-hot-knowledge-roots-routing-v1.md", "template-clean-setup-hot-clean-setup-routing-v1.md")) {
        $p = Join-Path $newcomer $n
        if (Test-Path -LiteralPath $p) { $hot += (Get-Content $p -Raw -Encoding UTF8) }
    }
    $body = (@(
            "# Agent notes", "",
            "Personal canon. Public slice is a separate read-only root (kb-public).", "",
            "<!-- public-cut -->", "", $hot
        ) -join "`n")
    Write-Utf8File $personalMarker $body
}

$tkMapDir = Join-Path $taskDst "work\local"
if (-not (Test-Path -LiteralPath $tkMapDir)) {
    if (-not $WhatIf) { New-Item -ItemType Directory -Force -Path $tkMapDir | Out-Null }
    $srcMap = Join-Path $notesDst "knowledge\work\local\workspace-scope-map-v1.md"
    if ((Test-Path -LiteralPath $srcMap) -and -not $WhatIf) {
        Copy-Item $srcMap (Join-Path $tkMapDir "workspace-scope-map-v1.md") -Force
    }
}

$notesTomlText = @"
version = 1
[knowledge]
primary = "personal"
[knowledge.roots]
personal = "$(Convert-ToTomlPath $notesDst)"
public = "$(Convert-ToTomlPath $kbDst)"
[[knowledge.read_only]]
id = "public"
path = "$(Convert-ToTomlPath $kbDst)"
[workspace]
default_scope = "mixed"
scope_map = "work/local/workspace-scope-map-v1.md"
scope_aliases = "work/local/scope-alias-map-v1.md"
"@
$taskTomlText = @"
version = 1
[task_knowledge]
primary = "personal"
[task_knowledge.roots]
personal = "$(Convert-ToTomlPath $taskDst)"
[workspace]
default_scope = "mixed"
scope_map = "work/local/workspace-scope-map-v1.md"
scope_aliases = "work/local/scope-alias-map-v1.md"
[workspace.store]
dir_name = ".task-knowledge"
mode = "scope_subdir"
scope_subdir = "scopes"
"@
$cdpTomlText = (Get-Content -LiteralPath $templateToml -Raw -Encoding UTF8).
    Replace("{notesToml}", (Convert-ToTomlPath $notesToml)).
    Replace("{taskToml}", (Convert-ToTomlPath $taskToml))

if (-not ($Upgrade -and (Test-Path -LiteralPath $cdpToml))) { Write-Utf8File $cdpToml $cdpTomlText }
if (-not ($Upgrade -and (Test-Path -LiteralPath $notesToml))) { Write-Utf8File $notesToml $notesTomlText }
if (-not ($Upgrade -and (Test-Path -LiteralPath $taskToml))) { Write-Utf8File $taskToml $taskTomlText }

$configArg = Convert-ToTomlPath $cdpToml
$snippetJson = (@{ mcpServers = @{ cdp = @{ command = $exe; args = @("--config", $configArg) } } } | ConvertTo-Json -Depth 8)
Write-Utf8File (Join-Path $cdpDst "host-snippets\cursor.mcp.json") $snippetJson
Write-Utf8File (Join-Path $cdpDst "host-snippets\claude.mcp.json") $snippetJson
Write-Utf8File (Join-Path $cdpDst "host-snippets\vscode.mcp.json") $snippetJson

switch ($HostAdapter) {
    "cursor" { Merge-McpServers (Join-Path $env:USERPROFILE ".cursor\mcp.json") $exe @("--config", $configArg); Write-Host "Reload MCP in Cursor." }
    "claude" { Merge-McpServers (Join-Path $env:APPDATA "Claude\claude_desktop_config.json") $exe @("--config", $configArg); Write-Host "Restart Claude Desktop." }
    "vscode" { Write-Host "VS Code: copy host-snippets\vscode.mcp.json into user MCP settings." }
    default { Write-Host "Host none — snippets under $cdpDst\host-snippets" }
}

Write-Host "OK. Payload $exe" -ForegroundColor Green
Write-Host "  public:   $kbDst"
Write-Host "  personal: $notesDst"
