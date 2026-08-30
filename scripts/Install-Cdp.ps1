<#
.SYNOPSIS
  Install CDP from a GitHub Release zip (no clone, no build).

.DESCRIPTION
  Downloads CdpMcp-*-{rid}.zip from AI-Guiders/cdp-mcp (win-x64 | linux-x64 | osx-x64 | osx-arm64),
  seeds kb-public + empty personal canon, merge host MCP json.
  Requires PowerShell 7+ on Mac/Linux (Windows PowerShell 5.1 OK on Windows).
  -CdpSource is an escape for a local published folder (maintainers).
  -Runtime overrides auto RID detection.
#>
[CmdletBinding()]
param(
    [string]$Root = "",
    [string]$CdpSource = "",
    [string]$ReleaseRepo = "AI-Guiders/cdp-mcp",
    [string]$ReleaseTag = "latest",
    [ValidateSet("", "win-x64", "linux-x64", "osx-x64", "osx-arm64")]
    [string]$Runtime = "",
    [string]$KbPublicRepo = "https://github.com/AI-Guiders/kb-public.git",
    [ValidateSet("cursor", "claude", "vscode", "windsurf", "antigravity", "opencode", "none")]
    [string]$HostAdapter = "cursor",
    [switch]$Upgrade,
    [switch]$SkipKbClone,
    [switch]$ForceDownload,
    [switch]$WhatIf
)

$ErrorActionPreference = "Stop"
$utf8 = New-Object System.Text.UTF8Encoding $false

# Windows PowerShell 5.1 has no $IsWindows / $IsMacOS / $IsLinux.
$script:IsWin = if ($null -ne (Get-Variable IsWindows -Scope Global -ErrorAction SilentlyContinue)) { [bool]$IsWindows } else { $true }
$script:IsMac = if ($null -ne (Get-Variable IsMacOS -Scope Global -ErrorAction SilentlyContinue)) { [bool]$IsMacOS } else { $false }
$script:IsLin = if ($null -ne (Get-Variable IsLinux -Scope Global -ErrorAction SilentlyContinue)) { [bool]$IsLinux } else { $false }

function Get-DefaultInstallRoot {
    if ($script:IsWin) {
        return (Join-Path $env:LOCALAPPDATA "AIGuiders")
    }
    if ($script:IsMac) {
        return (Join-Path $HOME "Library/Application Support/AIGuiders")
    }
    $xdg = if (-not [string]::IsNullOrWhiteSpace($env:XDG_DATA_HOME)) { $env:XDG_DATA_HOME } else { Join-Path $HOME ".local/share" }
    return (Join-Path $xdg "AIGuiders")
}

function Get-CdpRuntimeId {
    if (-not [string]::IsNullOrWhiteSpace($Runtime)) { return $Runtime }
    if ($script:IsWin) { return "win-x64" }
    if ($script:IsMac) {
        $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) { return "osx-arm64" }
        return "osx-x64"
    }
    if ($script:IsLin) {
        $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture
        if ($arch -eq [System.Runtime.InteropServices.Architecture]::Arm64) {
            throw "linux-arm64 is not shipped yet. Pass -CdpSource or build with -r linux-arm64."
        }
        return "linux-x64"
    }
    throw "Unsupported OS for CDP install."
}

function Get-CdpBinaryName {
    if ($script:IsWin) { return "CdpMcp.exe" }
    return "CdpMcp"
}

function Get-TempRoot {
    if (-not [string]::IsNullOrWhiteSpace($env:TEMP)) { return $env:TEMP }
    if (-not [string]::IsNullOrWhiteSpace($env:TMPDIR)) { return $env:TMPDIR }
    return [System.IO.Path]::GetTempPath()
}

function Get-HomeDir {
    if (-not [string]::IsNullOrWhiteSpace($HOME)) { return $HOME }
    if (-not [string]::IsNullOrWhiteSpace($env:USERPROFILE)) { return $env:USERPROFILE }
    return [Environment]::GetFolderPath("UserProfile")
}

function Write-Utf8File([string]$Path, [string]$Text) {
    if ($WhatIf) { Write-Host "WhatIf: write $Path"; return }
    $dir = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $dir)) { New-Item -ItemType Directory -Force -Path $dir | Out-Null }
    $normalized = $Text.Replace("`r`n", "`n")
    if ($script:IsWin) { $normalized = $normalized.Replace("`n", "`r`n") }
    [System.IO.File]::WriteAllText($Path, $normalized, $utf8)
}

function Convert-ToTomlPath([string]$Path) { return ($Path -replace "\\", "/") }

function Copy-CdpPayload([string]$Source, [string]$Destination) {
    if ($WhatIf) {
        Write-Host "WhatIf: copy payload $Source -> $Destination"
        return
    }
    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    if ($script:IsWin -and (Get-Command robocopy.exe -ErrorAction SilentlyContinue)) {
        & robocopy.exe $Source $Destination /E /XD ts-worker.node_modules /NFL /NDL /NJH /NJS /NP /R:1 /W:1 | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "robocopy payload failed ($LASTEXITCODE)" }
        return
    }
    # Cross-platform: mirror without following into a nested node_modules tree named ts-worker.node_modules
    Get-ChildItem -LiteralPath $Source -Force | ForEach-Object {
        $destItem = Join-Path $Destination $_.Name
        if ($_.PSIsContainer -and $_.Name -eq "ts-worker.node_modules") { return }
        if ($_.PSIsContainer) {
            if (Test-Path -LiteralPath $destItem) { Remove-Item -LiteralPath $destItem -Recurse -Force }
            Copy-Item -LiteralPath $_.FullName -Destination $destItem -Recurse -Force
        }
        else {
            Copy-Item -LiteralPath $_.FullName -Destination $destItem -Force
        }
    }
}

function Set-CdpExecutableBit([string]$BinaryPath) {
    if ($script:IsWin -or $WhatIf) { return }
    if (-not (Test-Path -LiteralPath $BinaryPath)) { return }
    & chmod +x $BinaryPath
    if ($LASTEXITCODE -ne 0) { Write-Host "WARN: chmod +x failed for $BinaryPath" -ForegroundColor Yellow }
}

function Get-CdpGithubPayload([string]$Rid) {
    $api = if ($ReleaseTag -eq "latest") {
        "https://api.github.com/repos/$ReleaseRepo/releases/latest"
    } else {
        "https://api.github.com/repos/$ReleaseRepo/releases/tags/$ReleaseTag"
    }
    $headers = @{ "User-Agent" = "Install-Cdp"; Accept = "application/vnd.github+json" }
    Write-Host "GitHub $api"
    $tempRoot = Get-TempRoot
    if ($WhatIf) {
        Write-Host "WhatIf: download CdpMcp-*-$Rid.zip"
        return (Join-Path $tempRoot "cdp-payload-whatif")
    }
    $rel = Invoke-RestMethod -Uri $api -Headers $headers
    $pattern = "^CdpMcp-.*-$([regex]::Escape($Rid))\.zip$"
    $asset = @($rel.assets) | Where-Object { $_.name -match $pattern } | Select-Object -First 1
    if (-not $asset) {
        throw "Release $($rel.tag_name) has no CdpMcp-*-$Rid.zip. Wait for CI or pass -CdpSource."
    }
    $zip = Join-Path $tempRoot $asset.name
    Write-Host "Download $($asset.name)"
    Invoke-WebRequest -Uri $asset.browser_download_url -OutFile $zip -Headers @{ "User-Agent" = "Install-Cdp" }
    $dest = Join-Path $tempRoot ("cdp-payload-" + $rel.tag_name + "-" + $Rid)
    if (Test-Path -LiteralPath $dest) { Remove-Item -LiteralPath $dest -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Expand-Archive -LiteralPath $zip -DestinationPath $dest -Force
    $binName = Get-CdpBinaryName
    $exeHit = Get-ChildItem -LiteralPath $dest -Filter $binName -Recurse -File | Select-Object -First 1
    if (-not $exeHit) { throw "Zip had no $binName`: $zip" }
    return $exeHit.DirectoryName
}

function Resolve-CdpSourceFolder([string]$Rid) {
    $binName = Get-CdpBinaryName
    if (-not [string]::IsNullOrWhiteSpace($CdpSource)) {
        return (Resolve-Path -LiteralPath $CdpSource).Path
    }
    $existing = Join-Path (Join-Path $Root "cdp") $binName
    if (-not $ForceDownload -and (Test-Path -LiteralPath $existing)) {
        return (Split-Path -Parent $existing)
    }
    return Get-CdpGithubPayload $Rid
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
    Write-Host "Merged mcp key 'cdp' -> $TargetPath"
}

function Get-CursorMcpPath {
    return (Join-Path (Join-Path (Get-HomeDir) ".cursor") "mcp.json")
}

function Get-ClaudeConfigPath {
    if ($script:IsWin) {
        return (Join-Path (Join-Path $env:APPDATA "Claude") "claude_desktop_config.json")
    }
    if ($script:IsMac) {
        return (Join-Path (Get-HomeDir) "Library/Application Support/Claude/claude_desktop_config.json")
    }
    return (Join-Path (Get-HomeDir) ".config/Claude/claude_desktop_config.json")
}

function Get-WindsurfMcpPath {
    # Codeium path survived rebrand; file is mcp_config.json (not mcp.json).
    return (Join-Path (Join-Path (Join-Path (Get-HomeDir) ".codeium") "windsurf") "mcp_config.json")
}

function Get-OpencodeConfigPath {
    # Global SSOT: ~/.config/opencode/opencode.jsonc (XDG on *nix; %USERPROFILE%\.config on Windows).
    $xdg = if (-not [string]::IsNullOrWhiteSpace($env:XDG_CONFIG_HOME)) { $env:XDG_CONFIG_HOME } else { Join-Path (Get-HomeDir) ".config" }
    return (Join-Path (Join-Path $xdg "opencode") "opencode.jsonc")
}

function Get-AntigravityMcpPath {
    # Antigravity 2.x shared: ~/.gemini/config/mcp_config.json
    # Older IDE-only: ~/.gemini/antigravity/mcp_config.json — prefer existing file.
    $gemini = Join-Path (Get-HomeDir) ".gemini"
    $shared = Join-Path (Join-Path $gemini "config") "mcp_config.json"
    $legacy = Join-Path (Join-Path $gemini "antigravity") "mcp_config.json"
    if (Test-Path -LiteralPath $shared) { return $shared }
    if (Test-Path -LiteralPath $legacy) { return $legacy }
    return $shared
}

if ([string]::IsNullOrWhiteSpace($Root)) { $Root = Get-DefaultInstallRoot }
$rid = Get-CdpRuntimeId
$binName = Get-CdpBinaryName

$templateToml = Join-Path $PSScriptRoot "cdp-mcp.toml.example"
if (-not (Test-Path -LiteralPath $templateToml)) {
    $fallback = Join-Path (Get-TempRoot) "cdp-mcp.toml.example"
    Write-Host "Fetch toml example from GitHub"
    if (-not $WhatIf) {
        Invoke-WebRequest "https://raw.githubusercontent.com/AI-Guiders/cdp-mcp/main/scripts/cdp-mcp.toml.example" -OutFile $fallback -Headers @{ "User-Agent" = "Install-Cdp" }
    }
    $templateToml = $fallback
}

$cdpSrc = Resolve-CdpSourceFolder $rid
$cdpDst = Join-Path $Root "cdp"
$kbDst = Join-Path $Root "kb-public"
$notesDst = Join-Path $Root "agent-notes"
$taskDst = Join-Path $Root "task-knowledge"
$notesToml = Join-Path $cdpDst "agent-notes-mcp.toml"
$taskToml = Join-Path $cdpDst "agent-task-knowledge-mcp.toml"
$cdpToml = Join-Path $cdpDst "cdp-mcp.toml"
$exe = Join-Path $cdpDst $binName

Write-Host "Install CDP -> $Root" -ForegroundColor Cyan
Write-Host "  rid:    $rid"
Write-Host "  source: $cdpSrc"
Write-Host "  host:   $HostAdapter"
Write-Host "  binary: $binName"

$preserveToml = @{}
if ($Upgrade -and -not $WhatIf -and (Test-Path -LiteralPath $cdpDst)) {
    Get-ChildItem -LiteralPath $cdpDst -Filter "*.toml" -File -ErrorAction SilentlyContinue | ForEach-Object {
        $preserveToml[$_.Name] = Get-Content -LiteralPath $_.FullName -Raw -Encoding UTF8
    }
}

if (-not $WhatIf) {
    New-Item -ItemType Directory -Force -Path $cdpDst, $notesDst, $taskDst | Out-Null
}
Copy-CdpPayload $cdpSrc $cdpDst
if (-not $WhatIf) {
    foreach ($kv in $preserveToml.GetEnumerator()) {
        [System.IO.File]::WriteAllText((Join-Path $cdpDst $kv.Key), $kv.Value, $utf8)
    }
    Set-CdpExecutableBit $exe
}

if (-not $SkipKbClone) {
    if (Test-Path -LiteralPath (Join-Path $kbDst ".git")) {
        Write-Host "kb-public exists - git pull"
        if (-not $WhatIf) {
            git -C $kbDst pull --ff-only
            if ($LASTEXITCODE -ne 0) { throw "git pull kb-public failed" }
        }
    }
    else {
        Write-Host "Clone kb-public -> $kbDst"
        if (-not $WhatIf) {
            git clone --depth 1 $KbPublicRepo $kbDst
            if ($LASTEXITCODE -ne 0) { throw "git clone kb-public failed" }
        }
    }
}

$personalMarker = Join-Path $notesDst "agent-notes.md"
if (-not (Test-Path -LiteralPath $personalMarker)) {
    Write-Host "Seed personal canon from kb-public templates/newcomer"
    $newcomer = Join-Path (Join-Path (Join-Path $kbDst "knowledge") "templates") "newcomer"
    $metaSrc = Join-Path (Join-Path $kbDst "knowledge") "META"
    $localDir = Join-Path (Join-Path (Join-Path $notesDst "knowledge") "work") "local"
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
            Copy-Item $metaSrc (Join-Path (Join-Path $notesDst "knowledge") "META") -Recurse -Force
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

$tkMapDir = Join-Path (Join-Path $taskDst "work") "local"
if (-not (Test-Path -LiteralPath $tkMapDir)) {
    if (-not $WhatIf) { New-Item -ItemType Directory -Force -Path $tkMapDir | Out-Null }
    $srcMap = Join-Path (Join-Path (Join-Path (Join-Path $notesDst "knowledge") "work") "local") "workspace-scope-map-v1.md"
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
$snippetsDir = Join-Path $cdpDst "host-snippets"
if (-not $WhatIf) { New-Item -ItemType Directory -Force -Path $snippetsDir | Out-Null }
Write-Utf8File (Join-Path $snippetsDir "cursor.mcp.json") $snippetJson
Write-Utf8File (Join-Path $snippetsDir "claude.mcp.json") $snippetJson
Write-Utf8File (Join-Path $snippetsDir "vscode.mcp.json") $snippetJson
Write-Utf8File (Join-Path $snippetsDir "windsurf.mcp.json") $snippetJson
Write-Utf8File (Join-Path $snippetsDir "antigravity.mcp.json") $snippetJson
# OpenCode uses mcp.<name> local command[] (not mcpServers) — see Merge-OpencodeMcp.
$opencodeSnippet = (@{
    mcp = @{
        cdp = @{
            type = "local"
            enabled = $true
            timeout = 60000
            command = @($exe, "--config", $configArg)
        }
    }
} | ConvertTo-Json -Depth 8)
Write-Utf8File (Join-Path $snippetsDir "opencode.mcp.json") $opencodeSnippet

switch ($HostAdapter) {
    "cursor" { Merge-McpServers (Get-CursorMcpPath) $exe @("--config", $configArg); Write-Host "Reload MCP in Cursor." }
    "claude" { Merge-McpServers (Get-ClaudeConfigPath) $exe @("--config", $configArg); Write-Host "Restart Claude Desktop." }
    "vscode" { Write-Host "VS Code: copy host-snippets/vscode.mcp.json into user MCP settings." }
    "windsurf" {
        Merge-McpServers (Get-WindsurfMcpPath) $exe @("--config", $configArg)
        Write-Host "Refresh MCP in Windsurf Cascade (Manage MCPs -> Refresh)."
        Write-Host "WARN: Windsurf caps ~100 tools across all MCP servers - CDP shortlists, but heavy mounts may hit the ceiling." -ForegroundColor Yellow
    }
    "antigravity" {
        Merge-McpServers (Get-AntigravityMcpPath) $exe @("--config", $configArg)
        Write-Host "Refresh MCP in Antigravity (MCP Store / View raw config). Path: $(Get-AntigravityMcpPath)"
    }
    "opencode" {
        $ocPath = Get-OpencodeConfigPath
        Write-Host "OpenCode: paste mcp.cdp from host-snippets/opencode.mcp.json into $ocPath (JSONC - merge by hand if comments present)."
        Write-Host "  snippet: $(Join-Path $snippetsDir 'opencode.mcp.json')"
        Write-Host "  AutoI wake: cdp_ignite op=arm harness=opencode session=ses_… when=timer in=5m task=…"
    }
    default { Write-Host "Host none - snippets under $snippetsDir" }
}

Write-Host "OK. Payload $exe" -ForegroundColor Green
Write-Host "  public:   $kbDst"
Write-Host "  personal: $notesDst"
