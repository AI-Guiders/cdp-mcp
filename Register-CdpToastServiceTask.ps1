# Register autostart for CdpToastService (CDP-ADR-0228).
# Usage: .\Register-CdpToastServiceTask.ps1 [-Uninstall] [-StartupFolderOnly]
# Prefers a scheduled task (AtLogOn, restart on failure); falls back to a
# Startup-folder shortcut (user scope, no elevation) — same pattern as
# Register-CdpGatekeeperLogonTask.ps1. The service must run in the interactive
# user session (WinRT toasts), so AtLogOn/Startup is the right trigger.

param(
    [switch]$Uninstall,
    [switch]$StartupFolderOnly,
    [string]$Exe = "D:\Experiments\Personal Cursor Folder\open\cdp-mcp\CdpToastService\publish\CdpToastService.exe",
    [string]$TaskName = "CdpToastService"
)

$ErrorActionPreference = "Stop"

$startupDir = [Environment]::GetFolderPath("Startup")
$startupLink = Join-Path $startupDir "$TaskName.lnk"

function Remove-ToastAutostart {
    Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $startupLink -Force -ErrorAction SilentlyContinue
}

if ($Uninstall) {
    Remove-ToastAutostart
    Write-Host "Removed autostart: $TaskName"
    exit 0
}

if (-not (Test-Path $Exe)) {
    throw "CdpToastService.exe not found: $Exe"
}

# Wrapper: start hidden (console app must not flash a window at logon).
$exeDir = Split-Path $Exe
$wrapper = Join-Path $exeDir "start-hidden.ps1"
@"
Start-Process -FilePath '$Exe' -WorkingDirectory '$exeDir' -WindowStyle Hidden
"@ | Set-Content -Path $wrapper -Encoding UTF8

$shell = if (Get-Command pwsh.exe -ErrorAction SilentlyContinue) { "pwsh.exe" } else { "powershell.exe" }
$argList = @(
    "-NoProfile",
    "-ExecutionPolicy", "Bypass",
    "-WindowStyle", "Hidden",
    "-File", "`"$wrapper`""
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
            -RestartCount 3 `
            -RestartInterval (New-TimeSpan -Minutes 1) `
            -ExecutionTimeLimit ([TimeSpan]::Zero)
        Register-ScheduledTask `
            -TaskName $TaskName `
            -Action $action `
            -Trigger $trigger `
            -Settings $settings `
            -Description "CDP-ADR-0228 operator toast service (Windows Notification Center subscriber)" `
            -Force | Out-Null
        $registered = $true
        Write-Host "Registered scheduled task: $TaskName"
    } catch {
        Write-Warning "Scheduled task failed ($($_.Exception.Message)) — falling back to Startup shortcut."
    }
}

if (-not $registered) {
    $wsh = New-Object -ComObject WScript.Shell
    $link = $wsh.CreateShortcut($startupLink)
    $link.TargetPath = $shell
    $link.Arguments = $argString
    $link.WindowStyle = 7
    $link.Description = "Start CdpToastService (CDP-ADR-0228) at logon"
    $link.Save()
    Write-Host "Registered Startup shortcut: $startupLink"
}
