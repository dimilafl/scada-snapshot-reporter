param(
    [string] $RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
)

$ErrorActionPreference = 'Stop'

function Assert-Contains {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Content,

        [Parameter(Mandatory = $true)]
        [string] $Expected
    )

    if (-not $Content.Contains($Expected)) {
        throw "Expected output to contain: $Expected"
    }
}

Push-Location $RepositoryRoot
try {
    dotnet build .\src\OtSnapshotReporter\OtSnapshotReporter.csproj | Write-Host
    dotnet build .\src\OtSnapshotGui\OtSnapshotGui.csproj | Write-Host

    $testOutput = Join-Path $RepositoryRoot 'Output\test-drift'
    if (Test-Path $testOutput) {
        Remove-Item -LiteralPath $testOutput -Recurse -Force
    }

    dotnet run --project .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -- --input .\samples\drift-current --previous .\samples\previous --config .\config --output $testOutput | Write-Host

    $latest = Get-ChildItem -Path $testOutput -Directory | Sort-Object Name -Descending | Select-Object -First 1
    if ($null -eq $latest) {
        throw 'Reporter did not create an output folder.'
    }

    $exceptions = Get-Content -Path (Join-Path $latest.FullName 'exceptions.csv') -Raw
    Assert-Contains -Content $exceptions -Expected 'Service status changed from Running to Stopped'
    Assert-Contains -Content $exceptions -Expected 'Free space dropped by 16 percentage points since previous snapshot'
    Assert-Contains -Content $exceptions -Expected 'Task action changed since previous snapshot'
    Assert-Contains -Content $exceptions -Expected 'Last task result was 1'
    Assert-Contains -Content $exceptions -Expected 'Version changed from 1.0.0 to 1.1.0'
    Assert-Contains -Content $exceptions -Expected 'Version changed from 11.0 to 11.1'
    Assert-Contains -Content $exceptions -Expected 'Server rebooted since previous snapshot'
    Assert-Contains -Content $exceptions -Expected 'critical events in the last 24 hours'
    Assert-Contains -Content $exceptions -Expected 'Share is unreachable'
    Assert-Contains -Content $exceptions -Expected 'Newest file is 57.5 hours old; threshold is 24 hours'
    Assert-Contains -Content $exceptions -Expected 'Backup/export folder is empty'
    Assert-Contains -Content $exceptions -Expected 'Event count rose from 4 to 12'
    Assert-Contains -Content $exceptions -Expected 'Share was reachable previously, now unreachable'
    Assert-Contains -Content $exceptions -Expected 'Newest file age increased from 10h to 57.5h'
    Assert-Contains -Content $exceptions -Expected 'DSN connection test failed'
    Assert-Contains -Content $exceptions -Expected 'Certificate expires in 17 days'

    $matrix = Get-Content -Path (Join-Path $latest.FullName 'software_matrix.html') -Raw
    Assert-Contains -Content $matrix -Expected '<th>Component</th><th>localhost</th><th>Expected</th>'
    Assert-Contains -Content $matrix -Expected '<td>Vendor Client</td><td>1.1.0</td>'

    $driverMatrix = Get-Content -Path (Join-Path $latest.FullName 'odbc_oledb_inventory.html') -Raw
    Assert-Contains -Content $driverMatrix -Expected '<h1>ODBC/OLE DB Matrix</h1>'
    Assert-Contains -Content $driverMatrix -Expected '<td>ODBC | SQL Server Native Client | 64-bit</td><td>11.1</td>'

    foreach ($reportName in @('event_log_summary.html', 'file_shares.html', 'backup_freshness.html', 'odbc_dsn_tests.html', 'certificates.html')) {
        if (-not (Test-Path (Join-Path $latest.FullName $reportName))) {
            throw "Expected report page was not generated: $reportName"
        }
    }

    $sampleOutput = Join-Path $RepositoryRoot 'Output\test-backup-stale'
    $backupConfig = Join-Path $RepositoryRoot 'Output\test-backup-stale-config'
    $sampleInput = Join-Path $RepositoryRoot 'Output\test-backup-stale-input'
    foreach ($path in @($sampleOutput, $backupConfig, $sampleInput)) {
        if (Test-Path $path) { Remove-Item -LiteralPath $path -Recurse -Force }
    }
    New-Item -ItemType Directory -Path $backupConfig -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $sampleInput 'raw') -Force | Out-Null
    Copy-Item -Path (Join-Path $RepositoryRoot 'config\*') -Destination $backupConfig -Recurse -Force
    Copy-Item -Path (Join-Path $RepositoryRoot 'samples\drift-current\raw\*') -Destination (Join-Path $sampleInput 'raw') -Recurse -Force

    $sampleBackupRows = @(
        @{
            server = 'localhost'
            name = 'StaleExport'
            path = 'C:\Exports'
            maxAgeHours = 1.0
            exists = $true
            newestFile = 'C:\Exports\export.csv'
            newestWriteTime = (Get-Date).AddHours(-5).ToString('s')
            ageHours = 5.0
            error = $null
        }
    )
    ConvertTo-Json -InputObject $sampleBackupRows | Set-Content -Path (Join-Path $sampleInput 'raw\backup_freshness.json') -Encoding UTF8
    @{ paths = @(@{ name = 'StaleExport'; path = 'C:\Exports'; max_age_hours = 1.0 }) } |
        ConvertTo-Json |
        Set-Content -Path (Join-Path $backupConfig 'expected_paths.json') -Encoding UTF8

    dotnet run --project .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -- --input $sampleInput --config $backupConfig --output $sampleOutput | Write-Host
    $staleLatest = Get-ChildItem -Path $sampleOutput -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $staleCsv = Get-Content -Path (Join-Path $staleLatest.FullName 'exceptions.csv') -Raw
    Assert-Contains -Content $staleCsv -Expected 'hours old; threshold is'

    $sampleShareInput = Join-Path $RepositoryRoot 'Output\test-share-input'
    if (Test-Path $sampleShareInput) { Remove-Item -LiteralPath $sampleShareInput -Recurse -Force }
    New-Item -ItemType Directory -Path (Join-Path $sampleShareInput 'raw') -Force | Out-Null
    Copy-Item -Path (Join-Path $RepositoryRoot 'samples\drift-current\raw\*') -Destination (Join-Path $sampleShareInput 'raw') -Recurse -Force
    $sampleShareRows = @(
        @{
            server = 'localhost'
            name = 'DeadShare'
            path = '\\dead\share'
            reachable = $false
            error = 'Network path not found'
            checkedAt = (Get-Date).ToString('s')
        }
    )
    ConvertTo-Json -InputObject $sampleShareRows | Set-Content -Path (Join-Path $sampleShareInput 'raw\file_shares.json') -Encoding UTF8
    @{ shares = @(@{ name = 'DeadShare'; path = '\\dead\share' }) } |
        ConvertTo-Json |
        Set-Content -Path (Join-Path $backupConfig 'shares.json') -Encoding UTF8

    dotnet run --project .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -- --input $sampleShareInput --config $backupConfig --output $sampleOutput | Write-Host
    $shareLatest = Get-ChildItem -Path $sampleOutput -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $shareCsv = Get-Content -Path (Join-Path $shareLatest.FullName 'exceptions.csv') -Raw
    Assert-Contains -Content $shareCsv -Expected 'Share is unreachable'

    $baselineConfig = Join-Path $RepositoryRoot 'Output\test-accept-baseline-config'
    $baselineOutput = Join-Path $RepositoryRoot 'Output\test-accept-baseline-output'
    if (Test-Path $baselineConfig) { Remove-Item -LiteralPath $baselineConfig -Recurse -Force }
    if (Test-Path $baselineOutput) { Remove-Item -LiteralPath $baselineOutput -Recurse -Force }
    New-Item -ItemType Directory -Path $baselineConfig -Force | Out-Null
    Copy-Item -Path (Join-Path $RepositoryRoot 'config\*') -Destination $baselineConfig -Recurse -Force
    dotnet run --project .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -- --input .\samples\drift-current --config $baselineConfig --output $baselineOutput --accept-baseline | Write-Host
    $acceptedDrivers = Get-Content -Path (Join-Path $baselineConfig 'expected_drivers.json') -Raw | ConvertFrom-Json
    if (-not ($acceptedDrivers.Drivers | Where-Object { $_.type -eq 'ODBC' -and $_.expected_version -eq '11.1' } | Select-Object -First 1)) {
        throw 'Expected --accept-baseline to write driver baseline config.'
    }

    $retentionConfig = Join-Path $RepositoryRoot 'Output\test-retention-config'
    $retentionOutput = Join-Path $RepositoryRoot 'Output\test-retention-output'
    if (Test-Path $retentionConfig) { Remove-Item -LiteralPath $retentionConfig -Recurse -Force }
    if (Test-Path $retentionOutput) { Remove-Item -LiteralPath $retentionOutput -Recurse -Force }
    New-Item -ItemType Directory -Path $retentionConfig -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $retentionOutput '2001-01-01_0000\raw') -Force | Out-Null
    Set-Content -Path (Join-Path $retentionOutput '2001-01-01_0000\index.html') -Value '<html></html>' -Encoding UTF8
    Copy-Item -Path (Join-Path $RepositoryRoot 'config\*') -Destination $retentionConfig -Recurse -Force
    @{
        disk_free_percent_warning = 15
        disk_free_percent_critical = 10
        task_not_run_hours_warning = 24
        reboot_detection_enabled = $true
        disk_drop_percent_warning = 10
        snapshot_retention_days = 1
    } | ConvertTo-Json | Set-Content -Path (Join-Path $retentionConfig 'thresholds.json') -Encoding UTF8
    dotnet run --project .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -- --input .\samples\demo --config $retentionConfig --output $retentionOutput | Write-Host
    if (Test-Path (Join-Path $retentionOutput '2001-01-01_0000')) {
        throw 'Expected snapshot retention cleanup to delete old dated report folder.'
    }

    $collectorOutput = Join-Path $RepositoryRoot 'Output\test-oledb'
    if (Test-Path $collectorOutput) {
        Remove-Item -LiteralPath $collectorOutput -Recurse -Force
    }

    .\collectors\Collect-OdbcOleDbDrivers.ps1 -ConfigPath .\config -OutputPath $collectorOutput
    $driverRows = Get-Content -Path (Join-Path $collectorOutput 'raw\odbc_oledb.json') -Raw | ConvertFrom-Json
    if (-not ($driverRows | Where-Object { $_.type -eq 'OLE DB' } | Select-Object -First 1)) {
        throw 'ODBC/OLE DB collector did not emit any OLE DB provider rows.'
    }

    $bootstrapConfig = Join-Path $RepositoryRoot 'Output\test-bootstrap-config'
    if (Test-Path $bootstrapConfig) {
        Remove-Item -LiteralPath $bootstrapConfig -Recurse -Force
    }

    .\collectors\Initialize-ExpectedConfig.ps1 -InputPath .\samples\drift-current -ConfigPath $bootstrapConfig
    $expectedServices = Get-Content -Path (Join-Path $bootstrapConfig 'expected_services.json') -Raw | ConvertFrom-Json
    $expectedDrivers = Get-Content -Path (Join-Path $bootstrapConfig 'expected_drivers.json') -Raw | ConvertFrom-Json
    if ($expectedServices.services.Count -ne 2) {
        throw "Expected bootstrap to create 2 service baselines, got $($expectedServices.services.Count)."
    }
    if (-not ($expectedDrivers.drivers | Where-Object { $_.type -eq 'ODBC' -and $_.expected_version -eq '11.1' } | Select-Object -First 1)) {
        throw 'Expected bootstrap to preserve driver type and expected version.'
    }

    . .\collectors\CollectorHelpers.ps1
    $errorOutput = Join-Path $RepositoryRoot 'Output\test-errors'
    if (Test-Path $errorOutput) {
        Remove-Item -LiteralPath $errorOutput -Recurse -Force
    }

    $cleanRows = Invoke-PerServer -Servers @('bad-server') -OutputPath $errorOutput -ScriptBlock {
        param($server)
        throw "Cannot reach $server"
    }
    Write-JsonOutput -Data $cleanRows -Path (Join-Path $errorOutput 'raw\fake.json')
    $fakeRows = Get-Content -Path (Join-Path $errorOutput 'raw\fake.json') -Raw | ConvertFrom-Json
    $errorRows = Get-Content -Path (Join-Path $errorOutput 'raw\_errors.json') -Raw | ConvertFrom-Json
    if (@($fakeRows).Count -ne 0) {
        throw 'Expected failed per-server collection to keep data output empty.'
    }
    if (-not (@($errorRows) | Where-Object { $_.server -eq 'bad-server' -and $_.error -like '*Cannot reach bad-server*' } | Select-Object -First 1)) {
        throw 'Expected failed per-server collection to write _errors.json.'
    }

    $errorInput = Join-Path $RepositoryRoot 'Output\test-error-input'
    $errorReport = Join-Path $RepositoryRoot 'Output\test-error-report'
    if (Test-Path $errorInput) { Remove-Item -LiteralPath $errorInput -Recurse -Force }
    if (Test-Path $errorReport) { Remove-Item -LiteralPath $errorReport -Recurse -Force }
    New-Item -Path (Join-Path $errorInput 'raw') -ItemType Directory -Force | Out-Null
    foreach ($rawFile in @('services.json', 'disk_space.json', 'scheduled_tasks.json', 'uptime.json', 'software.json', 'odbc_oledb.json', 'event_log_summary.json', 'file_shares.json', 'backup_freshness.json', 'odbc_dsn_tests.json', 'certificates.json')) {
        Set-Content -Path (Join-Path $errorInput "raw\$rawFile") -Value '[]' -Encoding UTF8
    }
    Set-Content -Path (Join-Path $errorInput 'raw\_errors.json') -Value '[{"server":"bad-server","error":"RPC unavailable"}]' -Encoding UTF8
    dotnet run --project .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -- --input $errorInput --config .\config --output $errorReport | Write-Host
    $errorLatest = Get-ChildItem -Path $errorReport -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $errorExceptions = Get-Content -Path (Join-Path $errorLatest.FullName 'exceptions.csv') -Raw
    Assert-Contains -Content $errorExceptions -Expected 'collection_errors,bad-server,Collector,Critical,RPC unavailable'
    if (-not (Test-Path (Join-Path $errorLatest.FullName 'errors.html'))) {
        throw 'Expected errors.html report page for collection errors.'
    }

    $discoverOutput = Join-Path $RepositoryRoot 'Output\test-discover'
    if (Test-Path $discoverOutput) {
        Remove-Item -LiteralPath $discoverOutput -Recurse -Force
    }

    .\collectors\Run-Collectors.ps1 -ConfigPath .\config -OutputPath $discoverOutput
    $expectedRawFiles = @(
        'backup_freshness.json',
        'certificates.json',
        'disk_space.json',
        'event_log_summary.json',
        'file_shares.json',
        'odbc_dsn_tests.json',
        'odbc_oledb.json',
        'scheduled_tasks.json',
        'services.json',
        'software.json',
        'uptime.json'
    )
    foreach ($rawFile in $expectedRawFiles) {
        if (-not (Test-Path (Join-Path $discoverOutput "raw\$rawFile"))) {
            throw "Discovery runner did not create expected raw output: $rawFile"
        }
    }

    $perServerOutput = Join-Path $RepositoryRoot 'Output\test-per-server'
    if (Test-Path $perServerOutput) {
        Remove-Item -LiteralPath $perServerOutput -Recurse -Force
    }
    .\collectors\Run-Collectors.ps1 -ConfigPath .\config -OutputPath $perServerOutput -PerServer
    $perServerRaw = Get-ChildItem -Path $perServerOutput -Directory -Recurse |
        Where-Object { $_.Name -eq 'raw' } |
        Select-Object -First 1
    if ($null -eq $perServerRaw) {
        throw 'Per-server collector mode did not create a nested raw folder.'
    }

    $resolveInput = Join-Path $RepositoryRoot 'Output\test-resolve-input'
    $resolveReport = Join-Path $RepositoryRoot 'Output\test-resolve-report'
    if (Test-Path $resolveInput) { Remove-Item -LiteralPath $resolveInput -Recurse -Force }
    if (Test-Path $resolveReport) { Remove-Item -LiteralPath $resolveReport -Recurse -Force }
    New-Item -ItemType Directory -Path (Join-Path $resolveInput 'SERVER01\raw') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $resolveInput 'SERVER02\raw') -Force | Out-Null
    Copy-Item -Path (Join-Path $RepositoryRoot 'samples\demo\*') -Destination (Join-Path $resolveInput 'SERVER01\raw') -Recurse -Force
    Copy-Item -Path (Join-Path $RepositoryRoot 'samples\demo\*') -Destination (Join-Path $resolveInput 'SERVER02\raw') -Recurse -Force
    Set-Content -Path (Join-Path $resolveInput 'SERVER02\raw\services.json') -Value '[]' -Encoding UTF8
    $serverTwoDiskRows = @(
        @{
            server = 'SERVER02'
            drive = 'Z:'
            totalGb = 100.0
            freeGb = 5.0
            freePercent = 5.0
        }
    )
    ConvertTo-Json -InputObject $serverTwoDiskRows | Set-Content -Path (Join-Path $resolveInput 'SERVER02\raw\disk_space.json') -Encoding UTF8
    dotnet run --project .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -- --input $resolveInput --config .\config --output $resolveReport | Write-Host
    $resolveLatest = Get-ChildItem -Path $resolveReport -Directory |
        Where-Object { Test-Path (Join-Path $_.FullName 'index.html') } |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if (-not (Test-Path (Join-Path $resolveLatest.FullName 'index.html'))) {
        throw 'Reporter did not resolve per-server raw input folder.'
    }
    $resolveCsv = Get-Content -Path (Join-Path $resolveLatest.FullName 'exceptions.csv') -Raw
    Assert-Contains -Content $resolveCsv -Expected 'disk,SERVER02,Z:,Critical,Free space is 5%'

    Get-ChildItem -Path .\collectors -Filter *.ps1 | ForEach-Object {
        $parseLexemes = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($_.FullName, [ref] $parseLexemes, [ref] $errors) > $null
        if ($errors.Count -gt 0) {
            throw "PowerShell parse failed for $($_.Name): $($errors[0].Message)"
        }
    }
}
finally {
    Pop-Location
}
