param(
    [string] $TaskName = 'OT Snapshot Reporter',
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path,
    [string] $ConfigPath = (Join-Path $RepositoryRoot 'config'),
    [string] $OutputRoot = (Join-Path $RepositoryRoot 'Output'),
    [Parameter(Mandatory = $true)]
    [string] $ReportExecutablePath,
    [string] $DailyAt = '06:00'
)

$ErrorActionPreference = 'Stop'

$runner = Join-Path $PSScriptRoot 'Run-ScheduledSnapshot.ps1'
$time = [DateTime]::ParseExact($DailyAt, 'HH:mm', [Globalization.CultureInfo]::InvariantCulture)
$argumentList = @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', "`"$runner`"",
    '-RepositoryRoot', "`"$RepositoryRoot`"",
    '-ConfigPath', "`"$ConfigPath`"",
    '-OutputRoot', "`"$OutputRoot`"",
    '-ReportExecutablePath', "`"$ReportExecutablePath`""
) -join ' '

$action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $argumentList
$trigger = New-ScheduledTaskTrigger -Daily -At $time
# SYSTEM runs with local admin + computer account network identity.
# For share-write access in a workgroup, change to a domain service account.
$principal = New-ScheduledTaskPrincipal -UserId "NT AUTHORITY\SYSTEM" -LogonType ServiceAccount -RunLevel Highest
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Hours 2)

Register-ScheduledTask -TaskName $TaskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
Write-Host "Registered scheduled task '$TaskName' to run daily at $DailyAt."
