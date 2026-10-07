<#
.SYNOPSIS
  Stops and removes the 'PhotoProcessing Watcher' scheduled task (elevates itself). Leaves the runtime folder alone.
#>
$ErrorActionPreference = 'Stop'
$taskName = 'PhotoProcessing Watcher'

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    $shell = (Get-Process -Id $PID).Path
    Start-Process $shell -Verb RunAs -Wait -ArgumentList @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"")
    return
}

if (Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue) {
    Stop-ScheduledTask -TaskName $taskName
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
    Get-Process photoedit -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Host "Removed '$taskName'"
} else {
    Write-Host "'$taskName' is not installed"
}
