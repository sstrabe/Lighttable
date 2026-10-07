<#
.SYNOPSIS
  Registers the 'PhotoProcessing Tray' scheduled task: the status icon, started at every logon (and now).
  No admin rights needed. Use -Uninstall to remove it.

.DESCRIPTION
  A scheduled task rather than a Run key or Startup shortcut: when this is run from inside the Claude
  desktop app (an MSIX package), writes to HKCU\Software and AppData are redirected into the app's
  private storage, where Explorer would never see them. Task registration isn't affected.
#>
param(
    [string]$BinDir = (Join-Path $env:USERPROFILE '.photoprocessing\bin'),
    [switch]$Uninstall
)
$ErrorActionPreference = 'Stop'
$taskName = 'PhotoProcessing Tray'

if ($Uninstall) {
    if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $taskName
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    }
    Get-Process photoedit-tray -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Host "Removed '$taskName'"
    return
}

$exe = Join-Path $BinDir 'photoedit-tray.exe'
if (-not (Test-Path $exe)) { throw "$exe not found; run scripts\publish.ps1 first" }

$user = "$env:USERDOMAIN\$env:USERNAME"
$action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $BinDir
$trigger = New-ScheduledTaskTrigger -AtLogOn -User $user
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -ExecutionTimeLimit ([TimeSpan]::Zero) -MultipleInstances IgnoreNew -Priority 5
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Limited

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Settings $settings `
    -Principal $principal -Force -Description 'Notification-area status icon for the PhotoProcessing watcher.' | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Host "Registered and started '$taskName'."
