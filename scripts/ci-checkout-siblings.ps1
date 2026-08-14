<#
  Layout expected by CdpMcp.csproj: sibling folders next to cdp-mcp.
  Run from GITHUB_WORKSPACE after actions/checkout path=cdp-mcp.
#>
[CmdletBinding()]
param(
    [string]$Workspace = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
Set-Location $Workspace

$gh = @(
    @{ Path = "cdp-core"; Repo = "AI-Guiders/cdp-core" },
    @{ Path = "cdp-scriptable-ide"; Repo = "AI-Guiders/cdp-scriptable-ide" },
    @{ Path = "agent-notes-mcp"; Repo = "AI-Guiders/agent-notes-mcp" },
    @{ Path = "agent-notes-core"; Repo = "AI-Guiders/AIGuiders.AgentNotes.Core" },
    @{ Path = "agent-task-knowledge-core"; Repo = "AI-Guiders/AIGuiders.AgentTaskKnowledge.Core" },
    @{ Path = "dotnet-build-test-core"; Repo = "AI-Guiders/dotnet-build-test-core" },
    @{ Path = "cdp-evidence"; Repo = "AI-Guiders/cdp-evidence" },
    @{ Path = "roslyn-mcp-core"; Repo = "AI-Guiders/roslyn-mcp-core" },
    @{ Path = "git-mcp-core"; Repo = "AI-Guiders/git-mcp-core" },
    @{ Path = "hybrid-codebase-index-core"; Repo = "AI-Guiders/hybrid-codebase-index-core" },
    @{ Path = "agent-task-knowledge-mcp"; Repo = "AI-Guiders/agent-task-knowledge-mcp" },
    @{ Path = "dotnet-debug-mcp"; Repo = "AI-Guiders/dotnet-debug-mcp" },
    @{ Path = "dotnet-build-test-mcp-repo"; Repo = "AI-Guiders/dotnet-build-test-mcp" },
    @{ Path = "roslyn-mcp"; Repo = "AI-Guiders/RoslynMcp" },
    @{ Path = "git-mcp"; Repo = "AI-Guiders/git-mcp" },
    @{ Path = "hybrid-codebase-index"; Repo = "AI-Guiders/hybrid-codebase-index" },
    @{ Path = "ai-native-ui"; Repo = "AI-Guiders/ai-native-ui" }
)

$token = $env:GH_PAT
if ([string]::IsNullOrWhiteSpace($token)) { $token = $env:GITHUB_TOKEN }
$prefix = if ($token) { "https://x-access-token:$token@github.com/" } else { "https://github.com/" }

foreach ($row in $gh) {
    $dest = Join-Path $Workspace $row.Path
    if (Test-Path -LiteralPath $dest) {
        Write-Host "exists $($row.Path)"
        continue
    }
    $url = $prefix + $row.Repo + ".git"
    Write-Host "clone $($row.Repo) → $($row.Path)"
    git clone --depth 1 $url $dest
    if ($LASTEXITCODE -ne 0) {
        throw "clone failed: $($row.Repo) (ai-native-ui is private — set GH_PAT with repo scope)"
    }
}

$gitlabDirs = @(
    "terminal-mcp-core",
    "agent-findings-core",
    "agent-failures-core",
    "dotnet-debug-core",
    "typescript-lang",
    "lsp-lang",
    "agent-findings-mcp",
    "agent-failures-mcp"
)
$missing = @($gitlabDirs | Where-Object { -not (Test-Path -LiteralPath (Join-Path $Workspace $_)) })
if ($missing.Count -eq 0) { return }

$cloneUrl = $env:GITLAB_CLONE_URL
if ([string]::IsNullOrWhiteSpace($cloneUrl) -and $env:GITLAB_TOKEN) {
    $cloneUrl = "https://oauth2:$($env:GITLAB_TOKEN)@193.124.113.7/Krawler/financial-open.git"
}
if ([string]::IsNullOrWhiteSpace($cloneUrl)) {
    throw "GitLab-only siblings missing ($($missing -join ', ')). Set secret GITLAB_CLONE_URL or GITLAB_TOKEN. GitHub-hosted CI cannot see the local open/ tree."
}

$tmp = Join-Path $Workspace "_financial-open"
if (-not (Test-Path -LiteralPath $tmp)) {
    Write-Host "clone financial-open for GitLab-only siblings"
    git clone --depth 1 $cloneUrl $tmp
    if ($LASTEXITCODE -ne 0) { throw "GitLab clone failed" }
}
foreach ($d in $missing) {
    $src = Join-Path $tmp $d
    if (-not (Test-Path -LiteralPath $src)) { throw "financial-open has no folder $d" }
    Write-Host "copy $d from financial-open"
    Copy-Item $src (Join-Path $Workspace $d) -Recurse -Force
}
