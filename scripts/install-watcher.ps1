<#
.SYNOPSIS
  Registers the 'PhotoProcessing Watcher' scheduled task: `photoedit watch`, started at boot (and at
  logon, in case it was stopped), running as you whether or not you're logged in, with no window.

.DESCRIPTION
  Uses the S4U logon type ("run whether user is logged on or not", without storing your Windows
  password). That works here because everything the watcher needs is file-based in your profile:
  the Claude Code login (~/.claude/.credentials.json) and the Heimdall sign-in for Nextcloud
  (~/.photoprocessing/state/heimdall.json, from `photoedit login`). Registering such a task needs
  admin rights, so the script elevates itself (one UAC prompt).
#>
param(
    [string]$BinDir = (Join-Path $env:USERPROFILE '.photoprocessing\bin')
)
$ErrorActionPreference = 'Stop'
$taskName = 'PhotoProcessing Watcher'
$exe = Join-Path $BinDir 'photoedit.exe'
if (-not (Test-Path $exe)) { throw "$exe not found; run scripts\publish.ps1 first" }

$isAdmin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole(
    [Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Host 'Elevating to register the task (approve the UAC prompt)...'
    $shell = (Get-Process -Id $PID).Path
    $proc = Start-Process $shell -Verb RunAs -Wait -PassThru -ArgumentList @(
        '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', "`"$PSCommandPath`"", '-BinDir', "`"$BinDir`"")
    if ($proc.ExitCode -ne 0) { throw "elevated install failed (exit $($proc.ExitCode))" }
    Get-ScheduledTask -TaskName $taskName | Select-Object TaskName, State
    return
}

$user = "$env:USERDOMAIN\$env:USERNAME"
$action = New-ScheduledTaskAction -Execute $exe -Argument 'watch' -WorkingDirectory $BinDir
$triggers = @(
    New-ScheduledTaskTrigger -AtStartup
    New-ScheduledTaskTrigger -AtLogOn -User $user
)
$settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
    -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero) `
    -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 5) -MultipleInstances IgnoreNew
$principal = New-ScheduledTaskPrincipal -UserId $user -LogonType S4U -RunLevel Limited

Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $triggers -Settings $settings `
    -Principal $principal -Force `
    -Description 'Polls the Nextcloud photo inbox and has Claude edit new raws with darktable.' | Out-Null
Start-ScheduledTask -TaskName $taskName
Write-Host "Registered and started '$taskName'. Logs: $(Join-Path $env:USERPROFILE '.photoprocessing\logs')"
