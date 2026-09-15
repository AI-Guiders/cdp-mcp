# Register autostart for CdpGatekeeper after user logon (ADR-0209 cold-boot gap).
# Prefers Scheduled Task; falls back to Startup folder shortcut if task registration is denied.
# Usage: .\Register-CdpGatekeeperLogonTask.ps1 [-Uninstall] [-StartupFolderOnly]
[CmdletBinding()]
param(
    [string] $TaskName = "CDP Gatekeeper",
    [string] $GatekeeperTarget = "D:\cdp-gatekeeper",
    [string] $Config = "D:\cdp-mcp\cdp-mcp.toml",
    [switch] $Uninstall,
    [switch] $StartupFolderOnly
)

$ErrorActionPreference = "Stop"

$sourceScript = Join-Path $PSScriptRoot "Start-CdpGatekeeper.ps1"
if (-not (Test-Path -LiteralPath $sourceScript)) {
    Write-Error "Missing $sourceScript"
}

$installedDir = Join-Path $GatekeeperTarget "scripts"
$installedScript = Join-Path $installedDir "Start-CdpGatekeeper.ps1"

$startupLink = Join-Path ([Environment]::GetFolderPath("Startup")) "CDP Gatekeeper.lnk"

function Remove-GatekeeperAutostart {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    if (Test-Path -LiteralPath $startupLink) { Remove-Item -LiteralPath $startupLink -Force }
}

function Install-StartupShortcut {
    param([string] $ShellExe, [string] $ArgumentList)
    $wsh = New-Object -ComObject WScript.Shell
    $link = $wsh.CreateShortcut($startupLink)
    $link.TargetPath = $ShellExe
    $link.Arguments = $ArgumentList
    $link.WindowStyle = 7
    $link.Description = "Start CdpGatekeeper (:8771) at logon"
    $link.Save()
    Write-Host "Registered Startup shortcut: $startupLink"
}

if ($Uninstall) {
    Remove-GatekeeperAutostart
    Write-Host "Removed autostart: $TaskName"
    exit 0
}

New-Item -ItemType Directory -Force -Path $installedDir | Out-Null
Copy-Item -LiteralPath $sourceScript -Destination $installedScript -Force

$shell = if (Get-Command pwsh.exe -ErrorAction SilentlyContinue) { "pwsh.exe" } else { "powershell.exe" }
$argList = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-WindowStyle", "Hidden",
    "-File", "`"$installedScript`"",
    "-Target", $GatekeeperTarget,
    "-Config", $Config
)
$argString = $argList -join " "

$registered = $false
if (-not $StartupFolderOnly) {
    try {
        $action = New-ScheduledTaskAction -Execute $shell -Argument $argString
        $trigger = New-ScheduledTaskTrigger -AtLogOn
        $settings = New-ScheduledTaskSettingsSet `
            -AllowStartIfOnBatteries `
            -DontStopIfGoingOnBatteries `
            -StartWhenAvailable `
            -MultipleInstances IgnoreNew `
            -ExecutionTimeLimit ([TimeSpan]::Zero)
        Register-ScheduledTask `
            -TaskName $TaskName `
            -Action $action `
            -Trigger $trigger `
            -Settings $settings `
            -Description "Start CdpGatekeeper (:8771) after user logon — ADR-0209 tower cold boot." `
            -Force | Out-Null
        Write-Host "Registered logon task: $TaskName"
        $registered = $true
    }
    catch {
        Write-Warning "Scheduled task registration failed ($($_.Exception.Message)); using Startup folder."
    }
}

if (-not $registered) {
    Install-StartupShortcut -ShellExe $shell -ArgumentList $argString
}

Write-Host "  script: $installedScript"
Write-Host "  target: $GatekeeperTarget"

& $installedScript -Target $GatekeeperTarget -Config $Config
Write-Host "Gatekeeper start probe finished (exit $LASTEXITCODE)."
