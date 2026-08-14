<#
  Layout expected by CdpMcp.csproj: sibling folders next to cdp-mcp.
  Run from GITHUB_WORKSPACE after actions/checkout path=cdp-mcp.
  GitHub-only — no GitLab / financial-open clone.
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
    @{ Path = "ai-native-ui"; Repo = "AI-Guiders/ai-native-ui" },
    @{ Path = "terminal-mcp-core"; Repo = "AI-Guiders/terminal-mcp-core" },
    @{ Path = "agent-findings-core"; Repo = "AI-Guiders/agent-findings-core" },
    @{ Path = "agent-findings-mcp"; Repo = "AI-Guiders/agent-findings-mcp" },
    @{ Path = "agent-failures-core"; Repo = "AI-Guiders/agent-failures-core" },
    @{ Path = "agent-failures-mcp"; Repo = "AI-Guiders/agent-failures-mcp" },
    @{ Path = "dotnet-debug-core"; Repo = "AI-Guiders/dotnet-debug-core" },
    @{ Path = "typescript-lang"; Repo = "AI-Guiders/typescript-lang" },
    @{ Path = "lsp-lang"; Repo = "AI-Guiders/lsp-lang" }
)

$token = $env:GH_PAT
if ([string]::IsNullOrWhiteSpace($token)) { $token = $env:GITHUB_TOKEN }
$prefix = if ($token) { "https://x-access-token:$token@github.com/" } else { "https://github.com/" }

$failed = @()
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
        $failed += $row.Repo
        Write-Host "WARN: clone failed $($row.Repo)" -ForegroundColor Yellow
    }
}

if ($failed.Count -gt 0) {
    throw ("GitHub sibling clone failed: " + ($failed -join ', ') + ". Create missing AI-Guiders/* repos (no GitLab). Private ai-native-ui needs secret GH_PAT with repo scope.")
}
