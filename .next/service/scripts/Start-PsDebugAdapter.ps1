#Requires -Version 7
# CDP: PowerShell Editor Services — DebugServiceOnly over stdio (PSES DAP).
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-PsEditorServices.ps1')
$pes = Resolve-CdpPsesBundle
Import-Module $pes.ModulePath -Force
$bundled = $pes.BundledModulesPath
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
