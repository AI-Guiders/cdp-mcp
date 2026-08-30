<#
  Siblings required to compile CdpMcp.csproj on GitHub-hosted runners.
  Run from GITHUB_WORKSPACE after actions/checkout path=cdp-mcp.

  Core packages (cdp-core, roslyn-mcp-core, …) come from NuGet on CI — not cloned here.
  Optional siblings (typescript-lang, guiders-core layout) warn-only.
#>
[CmdletBinding()]
param(
    [string]$Workspace = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
Set-Location $Workspace

# ProjectReference / Compile-linked sources in CdpMcp.csproj (release build).
$required = @(
    @{ Path = "agent-notes-mcp"; Repo = "AI-Guiders/agent-notes-mcp" },
    @{ Path = "agent-task-knowledge-mcp"; Repo = "AI-Guiders/agent-task-knowledge-mcp" },
    @{ Path = "dotnet-debug-mcp"; Repo = "AI-Guiders/dotnet-debug-mcp" },
    @{ Path = "dotnet-build-test-mcp-repo"; Repo = "AI-Guiders/dotnet-build-test-mcp" },
    @{ Path = "roslyn-mcp"; Repo = "AI-Guiders/RoslynMcp" },
    @{ Path = "git-mcp"; Repo = "AI-Guiders/git-mcp" },
    @{ Path = "hybrid-codebase-index"; Repo = "AI-Guiders/hybrid-codebase-index" },
    @{ Path = "ai-native-ui"; Repo = "AI-Guiders/ai-native-ui" },
    @{ Path = "agent-findings-mcp"; Repo = "AI-Guiders/agent-findings-mcp" },
    @{ Path = "agent-failures-mcp"; Repo = "AI-Guiders/agent-failures-mcp" }
)

# Nice-to-have: ts-worker bundle, local core mirrors. NuGet / empty TS facet if missing.
$optional = @(
    @{ Path = "typescript-lang"; Repo = "AI-Guiders/typescript-lang" },
    @{ Path = "guiders-core"; Repo = "AI-Guiders/guiders-core" },
    @{ Path = "cdp-core"; Repo = "AI-Guiders/cdp-core" },
    @{ Path = "cdp-scriptable-ide"; Repo = "AI-Guiders/cdp-scriptable-ide" }
)

$token = $env:GH_PAT
if ([string]::IsNullOrWhiteSpace($token)) { $token = $env:GITHUB_TOKEN }
$prefix = if ($token) { "https://x-access-token:$token@github.com/" } else { "https://github.com/" }

function Invoke-SiblingClone([array]$Rows, [switch]$Required) {
    $failed = @()
    foreach ($row in $Rows) {
        $dest = Join-Path $Workspace $row.Path
        if (Test-Path -LiteralPath $dest) {
            Write-Host "exists $($row.Path)"
            continue
        }
        $url = $prefix + $row.Repo + ".git"
        Write-Host "clone $($row.Repo) -> $($row.Path)"
        git clone --depth 1 $url $dest
        if ($LASTEXITCODE -ne 0) {
            $failed += $row.Repo
            $level = if ($Required) { "ERROR" } else { "WARN" }
            Write-Host "$level`: clone failed $($row.Repo)" -ForegroundColor $(if ($Required) { "Red" } else { "Yellow" })
        }
    }
    return $failed
}

$requiredFailed = Invoke-SiblingClone $required -Required
Invoke-SiblingClone $optional | Out-Null

if ($requiredFailed.Count -gt 0) {
    throw ("Required GitHub sibling clone failed: " + ($requiredFailed -join ', ') + ". Set GH_PAT (repo scope) if a repo is private.")
}
