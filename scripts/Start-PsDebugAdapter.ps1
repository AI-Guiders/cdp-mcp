#Requires -Version 7
# CDP: PowerShell Editor Services — DebugServiceOnly over stdio (PSES DAP).
$ErrorActionPreference = 'Stop'
$mod = Get-Module PowerShellEditorServices -ListAvailable | Sort-Object Version -Descending | Select-Object -First 1
if (-not $mod) {
    Write-Error 'PowerShellEditorServices not installed. cdp_settings op=lsp_ensure id=powershell'
    exit 1
}
Import-Module $mod.Path -Force
$bundled = Split-Path $mod.Path -Parent
$log = Join-Path $env:TEMP "cdp-pes-dap-$PID.log"
$session = Join-Path $env:TEMP "cdp-pes-dap-$PID.json"
Start-EditorServices `
    -HostName 'cdp-debug' `
    -HostProfileId '00000000-0000-0000-0000-000000000000' `
    -BundledModulesPath $bundled `
    -LogPath $log `
    -SessionDetailsPath $session `
    -FeatureFlags @() `
    -AdditionalModules @() `
    -Stdio `
    -DebugServiceOnly
