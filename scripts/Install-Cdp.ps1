<#
.SYNOPSIS
  Install CDP from a GitHub Release zip (no clone, no build).

.DESCRIPTION
  Default: download CdpMcp-*-win-x64.zip from AI-Guiders/cdp-mcp latest release,
  seed kb-public + empty personal canon, merge host MCP json.
  -CdpSource is an escape for a local published folder (maintainers).
#>
[CmdletBinding()]
param(
    [string]$Root = (Join-Path $env:LOCALAPPDATA "AIGuiders"),
    [string]$CdpSource = "",
    [string]$ReleaseRepo = "AI-Guiders/cdp-mcp",
    [string]$ReleaseTag = "latest",
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

function Get-CdpGithubPayload {
    $api = if ($ReleaseTag -eq "latest") {
        "https://api.github.com/repos/$ReleaseRepo/releases/latest"
    } else {
        "https://api.github.com/repos/$ReleaseRepo/releases/tags/$ReleaseTag"
    }
    $headers = @{ "User-Agent" = "Install-Cdp"; Accept = "application/vnd.github+json" }
    Write-Host "GitHub $api"
    if ($WhatIf) { Write-Host "WhatIf: download CdpMcp-*-win-x64.zip"; return Join-Path $env:TEMP "cdp-payload-whatif" }
    $rel = Invoke-RestMethod -Uri $api -Headers $headers
    $asset = @($rel.assets) | Where-Object { $_.name -match '^CdpMcp-.*-win-x64\.zip$' } | Select-Object -First 1
    if (-not $asset) {
        throw "Release $($rel.tag_name) has no CdpMcp-*-win-x64.zip. Wait for CI or pass -CdpSource."
    }
    $zip = Join-Path $env:TEMP $asset.name
    Write-Host "Download $($asset.name)"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers @{ "User-Agent" = "Install-Cdp" }
    $dest = Join-Path $env:TEMP ("cdp-payload-" + $rel.tag_name)
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
    return Get-CdpGithubPayload
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
    Write-Host "Fetch toml example from GitHub"
    if (-not $WhatIf) {
        Invoke-WebRequest "https://raw.githubusercontent.com/AI-Guiders/cdp-mcp/main/scripts/cdp-mcp.toml.example" -OutFile $fallback -Headers @{ "User-Agent" = "Install-Cdp" }
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
