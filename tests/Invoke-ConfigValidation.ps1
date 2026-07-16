param(
    [string] $ConfigPath = ".\config",
    [switch] $CheckReachability
)

$ErrorActionPreference = 'Stop'
$errors = New-Object System.Collections.Generic.List[string]

function Add-Error([string] $Message) {
    $errors.Add($Message)
}

function Read-JsonFile([string] $Path) {
    if (-not (Test-Path $Path)) {
        Add-Error "Missing config file: $Path"
        return $null
    }

    try {
        return Get-Content -Path $Path -Raw | ConvertFrom-Json
    }
    catch {
        Add-Error "Invalid JSON in $Path`: $($_.Exception.Message)"
        return $null
    }
}

$serversConfig = Read-JsonFile (Join-Path $ConfigPath 'servers.json')
$thresholds = Read-JsonFile (Join-Path $ConfigPath 'thresholds.json')
$serverNames = @()
if ($serversConfig -and $serversConfig.servers) {
    $serverNames = @($serversConfig.servers | ForEach-Object { $_.name } | Where-Object { $_ })
}

if ($serverNames.Count -eq 0) {
    Add-Error "servers.json must contain at least one server name."
}

if ($thresholds) {
    if ($thresholds.disk_free_percent_critical -lt 0 -or $thresholds.disk_free_percent_warning -lt 0) {
        Add-Error "Disk thresholds must be non-negative."
    }
    if ($thresholds.disk_free_percent_critical -gt $thresholds.disk_free_percent_warning) {
        Add-Error "Critical disk threshold must be less than or equal to warning threshold."
    }
    if ($thresholds.task_not_run_hours_warning -lt 0) {
        Add-Error "task_not_run_hours_warning must be non-negative."
    }
    if ($thresholds.disk_drop_percent_warning -lt 0) {
        Add-Error "disk_drop_percent_warning must be non-negative."
    }
    if ($thresholds.snapshot_retention_days -lt 1) {
        Add-Error "snapshot_retention_days must be at least 1."
    }
}

foreach ($fileName in @('expected_services.json', 'expected_tasks.json', 'expected_software.json', 'expected_drivers.json')) {
    $doc = Read-JsonFile (Join-Path $ConfigPath $fileName)
    if (-not $doc) { continue }

    $collections = @('services', 'tasks', 'software', 'drivers')
    foreach ($collection in $collections) {
        if ($doc.PSObject.Properties[$collection]) {
            foreach ($item in @($doc.$collection)) {
                if ($item.server -and $serverNames.Count -gt 0 -and $serverNames -notcontains $item.server) {
                    Add-Error "$fileName references server '$($item.server)' that is not present in servers.json."
                }
            }
        }
    }
}

$maintenance = Join-Path $ConfigPath 'maintenance_windows.json'
if (Test-Path $maintenance) {
    $doc = Read-JsonFile $maintenance
    foreach ($window in @($doc.windows)) {
        $start = [datetime]::MinValue
        $end = [datetime]::MinValue
        if (-not [datetime]::TryParse($window.start, [ref] $start)) {
            Add-Error "Maintenance window '$($window.name)' has invalid start."
        }
        if (-not [datetime]::TryParse($window.end, [ref] $end)) {
            Add-Error "Maintenance window '$($window.name)' has invalid end."
        }
        if ($start -ne [datetime]::MinValue -and $end -ne [datetime]::MinValue -and $end -lt $start) {
            Add-Error "Maintenance window '$($window.name)' ends before it starts."
        }
    }
}

if ($CheckReachability) {
    foreach ($server in $serverNames) {
        if (-not (Test-Connection -ComputerName $server -Count 1 -Quiet -ErrorAction SilentlyContinue)) {
            Add-Error "Server '$server' is not reachable by ICMP ping."
        }
    }
}

if ($errors.Count -gt 0) {
    Write-Host "Config validation failed:" -ForegroundColor Red
    foreach ($errorItem in $errors) {
        Write-Host " - $errorItem" -ForegroundColor Red
    }
    exit 1
}

Write-Host "Config validation passed for $ConfigPath" -ForegroundColor Green
