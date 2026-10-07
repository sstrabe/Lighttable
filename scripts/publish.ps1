<#
.SYNOPSIS
  Builds photoedit and the tray icon (Release) into the runtime bin folder the scheduled tasks run from.
  Stops the running watcher and tray first so their files can be replaced, and restarts them afterwards.
#>
param(
    [string]$Destination = (Join-Path $env:USERPROFILE '.photoprocessing\bin')
)
$ErrorActionPreference = 'Stop'
$watcherTask = 'PhotoProcessing Watcher'
$trayTask = 'PhotoProcessing Tray'
$root = Split-Path $PSScriptRoot -Parent

function Stop-Task([string]$name) {
    $task = Get-ScheduledTask -TaskName $name -ErrorAction SilentlyContinue
    if ($task -and $task.State -eq 'Running') {
        Write-Host "Stopping $name..."
        try { Stop-ScheduledTask -TaskName $name } catch { Write-Warning "Stop-ScheduledTask ${name}: $_" }
        return $true
    }
    return $false
}

$restartWatcher = Stop-Task $watcherTask
$restartTray = (Stop-Task $trayTask) -or [bool](Get-ScheduledTask -TaskName $trayTask -ErrorAction SilentlyContinue)
# Either may also have been started by hand; free the binaries regardless.
Get-Process photoedit, photoedit-tray -ErrorAction SilentlyContinue | Where-Object { $_.Path -like "$Destination*" } | Stop-Process -Force
Start-Sleep -Milliseconds 500

foreach ($project in 'src\PhotoProcessing.Cli', 'src\PhotoProcessing.Tray') {
    dotnet publish (Join-Path $root $project) -c Release -o $Destination --nologo
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish $project failed" }
}
Write-Host "Published to $Destination"

foreach ($pair in @(@($restartWatcher, $watcherTask), @($restartTray, $trayTask))) {
    if (-not $pair[0]) { continue }
    try {
        Start-ScheduledTask -TaskName $pair[1]
        Write-Host "Restarted $($pair[1])"
    } catch {
        Write-Warning "Could not restart $($pair[1]) ($_)"
    }
}
