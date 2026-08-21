#Requires -Version 7
# CDP: resolve PowerShellEditorServices — PSGallery module or VS Code/Cursor extension bundle.
param([switch]$Probe)

$ErrorActionPreference = 'Stop'

function Resolve-CdpPsesBundle {
    if ($env:CDP_PSES_MODULE_PATH -and (Test-Path -LiteralPath $env:CDP_PSES_MODULE_PATH)) {
        $modulePath = $env:CDP_PSES_MODULE_PATH
        $bundled = if ($env:CDP_PSES_BUNDLED_MODULES) { $env:CDP_PSES_BUNDLED_MODULES } else { Split-Path $modulePath -Parent }
        return [pscustomobject]@{
            ModulePath         = $modulePath
            BundledModulesPath = $bundled
            Source             = 'env'
        }
    }

    $mod = Get-Module PowerShellEditorServices -ListAvailable |
        Sort-Object Version -Descending |
        Select-Object -First 1
    if ($mod) {
        return [pscustomobject]@{
            ModulePath         = $mod.Path
            BundledModulesPath = Split-Path $mod.Path -Parent
            Source             = 'module'
        }
    }

    $extensionRoots = @(
        (Join-Path $env:USERPROFILE '.cursor\extensions')
        (Join-Path $env:USERPROFILE '.vscode\extensions')
    )
    foreach ($root in $extensionRoots) {
        if (-not (Test-Path -LiteralPath $root)) { continue }
        $extDirs = Get-ChildItem -LiteralPath $root -Directory -Filter 'ms-vscode.powershell-*' -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending
        foreach ($ext in $extDirs) {
            $bundled = Join-Path $ext.FullName 'modules'
            $psd1 = Join-Path $bundled 'PowerShellEditorServices\PowerShellEditorServices.psd1'
            if (Test-Path -LiteralPath $psd1) {
                return [pscustomobject]@{
                    ModulePath         = $psd1
                    BundledModulesPath = $bundled
                    Source             = 'vscode-extension'
                    ExtensionRoot      = $ext.FullName
                }
            }
        }
    }

    throw 'PowerShellEditorServices not found. Install ms-vscode.powershell: cursor --install-extension ms-vscode.powershell — or cdp_settings op=lsp_ensure id=powershell'
}

if ($MyInvocation.InvocationName -ne '.' -and $Probe) {
    $r = Resolve-CdpPsesBundle
    @{
        ok      = $true
        source  = $r.Source
        module  = $r.ModulePath
        bundled = $r.BundledModulesPath
    } | ConvertTo-Json -Compress
    exit 0
}
