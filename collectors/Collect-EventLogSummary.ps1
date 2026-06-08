param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run",
    [int] $Hours = 24,
    [int] $MaxEvents = 1000
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$logConfigFile = Join-Path $ConfigPath 'event_log_config.json'
$logConfigs = @(
    [pscustomobject]@{ name = 'Application'; include_warnings = $false; max_events = $MaxEvents },
    [pscustomobject]@{ name = 'System'; include_warnings = $false; max_events = $MaxEvents }
)
$windowHours = $Hours

if (Test-Path $logConfigFile) {
    $logConfig = Get-Content -Path $logConfigFile -Raw | ConvertFrom-Json
    if ($null -ne $logConfig.logs -and $logConfig.logs.Count -gt 0) {
        $logConfigs = @($logConfig.logs)
    }
    if ($logConfig.window_hours) {
        $windowHours = $logConfig.window_hours
    }
}

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    Invoke-ServerScript -Server $server -ScriptBlock {
        param($recordServer, $logs, $hours)

        $start = (Get-Date).AddHours(-1 * $hours)
        foreach ($log in $logs) {
            $levels = @(1, 2)
            if ($log.include_warnings) {
                $levels += 3
            }
            $maxEvents = if ($log.max_events) { [int]@($log.max_events)[0] } else { 500 }

            $events = Get-WinEvent -FilterHashtable @{
                LogName = $log.name
                Level = $levels
                StartTime = $start
            } -MaxEvents $maxEvents -ErrorAction SilentlyContinue

            $events |
                Group-Object LogName, ProviderName, Level |
                Sort-Object Count -Descending |
                ForEach-Object {
                    $first = $_.Group | Sort-Object TimeCreated -Descending | Select-Object -First 1
                    [pscustomobject]@{
                        server = $recordServer
                        logName = $first.LogName
                        source = $first.ProviderName
                        level = $first.Level
                        count = $_.Count
                        newestTime = if ($first.TimeCreated) { $first.TimeCreated.ToString("s") } else { $null }
                        newestEventId = $first.Id
                        windowHours = $hours
                    }
                }
        }
    } -ArgumentList $server, (, $logConfigs), $windowHours
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\event_log_summary.json')
