param(
    [string] $InputPath = ".\Output\manual-run",
    [string] $ConfigPath = ".\config",
    [switch] $RunCollectors
)

$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\CollectorHelpers.ps1"

if ($RunCollectors) {
    & "$PSScriptRoot\Run-Collectors.ps1" -ConfigPath $ConfigPath -OutputPath $InputPath
}

$rawPath = Join-Path $InputPath 'raw'
if (-not (Test-Path $rawPath)) {
    throw "Raw snapshot folder not found: $rawPath"
}

Ensure-Directory -Path $ConfigPath

function Read-RawJson {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    $path = Join-Path $rawPath $Name
    if (-not (Test-Path $path)) {
        return @()
    }

    $items = Get-Content -Path $path -Raw | ConvertFrom-Json
    foreach ($item in @($items)) {
        Write-Output $item
    }
}

function Test-HasProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object] $InputObject,

        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    return $null -ne $InputObject.PSObject.Properties[$Name]
}

$services = Read-RawJson -Name 'services.json' |
    Where-Object { -not (Test-HasProperty -InputObject $_ -Name 'error') } |
    Sort-Object server, name |
    ForEach-Object {
        [pscustomobject]@{
            server = $_.server
            name = $_.name
            expected_status = $_.status
            expected_startup_type = $_.startupType
            severity_if_stopped = 'Critical'
        }
    }

$tasks = Read-RawJson -Name 'scheduled_tasks.json' |
    Where-Object { -not (Test-HasProperty -InputObject $_ -Name 'error') } |
    Sort-Object server, taskPath, taskName |
    ForEach-Object {
        [pscustomobject]@{
            server = $_.server
            task_path = $_.taskPath
            task_name = $_.taskName
            expected_enabled = $_.enabled
        }
    }

$software = Read-RawJson -Name 'software.json' |
    Where-Object { -not (Test-HasProperty -InputObject $_ -Name 'error') -and $_.name } |
    Sort-Object server, name |
    ForEach-Object {
        [pscustomobject]@{
            server = $_.server
            name = $_.name
            expected_version = $_.version
        }
    }

$drivers = Read-RawJson -Name 'odbc_oledb.json' |
    Where-Object { -not (Test-HasProperty -InputObject $_ -Name 'error') -and $_.name } |
    Sort-Object server, type, name, architecture |
    ForEach-Object {
        [pscustomobject]@{
            server = $_.server
            type = $_.type
            name = $_.name
            architecture = $_.architecture
            expected_version = $_.version
        }
    }

Write-JsonDocument -Data ([pscustomobject]@{ services = @($services) }) -Path (Join-Path $ConfigPath 'expected_services.json')
Write-JsonDocument -Data ([pscustomobject]@{ tasks = @($tasks) }) -Path (Join-Path $ConfigPath 'expected_tasks.json')
Write-JsonDocument -Data ([pscustomobject]@{ software = @($software) }) -Path (Join-Path $ConfigPath 'expected_software.json')
Write-JsonDocument -Data ([pscustomobject]@{ drivers = @($drivers) }) -Path (Join-Path $ConfigPath 'expected_drivers.json')
