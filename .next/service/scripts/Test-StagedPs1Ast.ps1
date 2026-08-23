#Requires -Version 7
# Git pre-commit: AST-parse staged .ps1/.psm1/.psd1 (same engine as cdp_buffer Ps1BufferDiagnostics).
param(
    [string]$RepoRoot = (git rev-parse --show-toplevel 2>$null)
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($RepoRoot)) { exit 0 }
$RepoRoot = $RepoRoot.Trim()

$staged = git -C $RepoRoot diff --cached --name-only --diff-filter=ACMR 2>$null
if (-not $staged) { exit 0 }

$hits = @($staged | Where-Object { $_ -match '\.(ps1|psm1|psd1)$' })
if ($hits.Count -eq 0) { exit 0 }

$fail = 0
foreach ($rel in $hits) {
    $full = Join-Path $RepoRoot $rel
    if (-not (Test-Path -LiteralPath $full)) { continue }
    $errs = $null
    $toks = $null
    [void][System.Management.Automation.Language.Parser]::ParseFile($full, [ref]$toks, [ref]$errs)
    if (-not $errs -or $errs.Count -eq 0) { continue }
    Write-Host "pre-commit: PS AST FAIL $rel"
    foreach ($e in $errs) {
        $line = $e.Extent.StartLineNumber
        Write-Host "  [F:$($rel -replace '\\','/'); L:$line] $($e.ErrorId): $($e.Message)"
    }
    $fail++
}

if ($fail -gt 0) {
    Write-Host "pre-commit: fix PowerShell syntax before commit ($fail file(s))."
    exit 1
}
exit 0
