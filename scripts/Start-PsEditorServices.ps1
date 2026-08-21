#Requires -Version 7
# CDP: PowerShell Editor Services — LanguageServiceOnly over stdio (PSES LSP).
$ErrorActionPreference = 'Stop'
$mod = Get-Module PowerShellEditorServices -ListAvailable | Sort-Object Version -Descending | Select-Object -First 1
if (-not $mod) {
    Write-Error 'PowerShellEditorServices not installed. cdp_settings op=lsp_ensure id=powershell'
    exit 1
}
Import-Module $mod.Path -Force
$bundled = Split-Path $mod.Path -Parent
$log = Join-Path $env:TEMP "cdp-pes-lsp-$PID.log"
$session = Join-Path $env:TEMP "cdp-pes-lsp-$PID.json"
Start-EditorServices `
    -HostName 'cdp-lsp' `
    -HostProfileId '00000000-0000-0000-0000-000000000000' `
    -BundledModulesPath $bundled `
    -LogPath $log `
    -SessionDetailsPath $session `
    -FeatureFlags @() `
    -AdditionalModules @() `
    -Stdio `
    -LanguageServiceOnly
