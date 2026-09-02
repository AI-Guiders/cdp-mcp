#Requires -Version 7
# CDP: PowerShell Editor Services — LanguageServiceOnly over stdio (PSES LSP).
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Resolve-PsEditorServices.ps1')
$pes = Resolve-CdpPsesBundle
Import-Module $pes.ModulePath -Force
$bundled = $pes.BundledModulesPath
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
