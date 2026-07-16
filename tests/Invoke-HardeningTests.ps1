param(
    [string] $OutputRoot = ".\Output\hardening"
)

$ErrorActionPreference = 'Stop'
$startTime = Get-Date
$script:testCount = 0
$script:passCount = 0

function Test-Case {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Name,

        [Parameter(Mandatory = $true)]
        [scriptblock] $Script
    )

    $script:testCount++
    Write-Host "  [$($script:testCount)] $Name..." -NoNewline
    try {
        & $Script
        $script:passCount++
        Write-Host " PASS" -ForegroundColor Green
    }
    catch {
        Write-Host " FAIL" -ForegroundColor Red
        Write-Host "    $($_.Exception.Message)" -ForegroundColor Red
        throw
    }
}

function Assert-FileExists {
    param([string] $Path)
    if (-not (Test-Path $Path)) {
        throw "Expected file not found: $Path"
    }
}

function Get-LatestReport {
    param([string] $Root)
    $latest = Get-ChildItem -Path $Root -Directory -ErrorAction SilentlyContinue |
        Where-Object { Test-Path (Join-Path $_.FullName 'index.html') } |
        Sort-Object Name -Descending |
        Select-Object -First 1
    if ($null -eq $latest) {
        throw "No report folder with index.html found under $Root"
    }
    return $latest.FullName
}

function Assert-Contains {
    param(
        [string] $Content,
        [string] $Expected
    )
    if (-not $Content.Contains($Expected)) {
        throw "Expected content to contain: $Expected"
    }
}

Write-Host "=== Hardening Test Suite ===" -ForegroundColor Cyan
Write-Host ""

if (Test-Path $OutputRoot) {
    Remove-Item -LiteralPath $OutputRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $OutputRoot -Force | Out-Null

Test-Case "Engine builds (debug)" {
    dotnet build .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -c Debug | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine build failed" }
}

Test-Case "GUI builds (debug)" {
    dotnet build .\src\OtSnapshotGui\OtSnapshotGui.csproj -c Debug | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "GUI build failed" }
}

Test-Case "Unit tests pass" {
    dotnet test .\tests\OtSnapshotReporter.Tests\OtSnapshotReporter.Tests.csproj | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Unit tests failed" }
}

Test-Case "Smoke tests pass" {
    .\tests\Invoke-SmokeTests.ps1 | Out-Null
}

$collectorRun = Join-Path $OutputRoot 'collector-run'
Test-Case "All collectors run against localhost" {
    .\collectors\Run-Collectors.ps1 -OutputPath $collectorRun | Out-Null
    $rawPath = Join-Path $collectorRun 'raw'
    Assert-FileExists $rawPath
    $jsonFiles = @(Get-ChildItem -Path $rawPath -Filter '*.json' | Where-Object { $_.Name -ne '_errors.json' })
    if ($jsonFiles.Count -lt 8) {
        throw "Expected at least 8 collector JSON files, got $($jsonFiles.Count)"
    }
}

Test-Case "Collector runner cleans reusable output when requested" {
    $cleanRunner = Join-Path $OutputRoot 'clean-output-runner'
    $cleanOutput = Join-Path $cleanRunner 'output'
    New-Item -ItemType Directory -Path $cleanOutput -Force | Out-Null
    Set-Content -Path (Join-Path $cleanOutput 'stale-marker.txt') -Value 'stale'
    Copy-Item .\collectors\Run-Collectors.ps1 $cleanRunner
    Set-Content -Path (Join-Path $cleanRunner 'Collect-CleanProbe.ps1') -Value @'
param(
    [string] $ConfigPath,
    [string] $OutputPath
)
Set-Content -Path (Join-Path $OutputPath 'fresh-marker.txt') -Value 'fresh'
exit 0
'@ -Encoding UTF8

    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $cleanRunner 'Run-Collectors.ps1') `
        -ConfigPath .\config -OutputPath $cleanOutput -CleanOutput 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Expected clean-output runner to pass, got $LASTEXITCODE" }
    if (Test-Path (Join-Path $cleanOutput 'stale-marker.txt')) { throw "Stale output was not removed" }
    Assert-FileExists (Join-Path $cleanOutput 'fresh-marker.txt')
}

Test-Case "Collector runner refuses unsafe cleanup paths" {
    $unsafeRunner = Join-Path $OutputRoot 'unsafe-clean-runner'
    New-Item -ItemType Directory -Path $unsafeRunner -Force | Out-Null
    Copy-Item .\collectors\Run-Collectors.ps1 $unsafeRunner
    $filesystemRoot = [System.IO.Path]::GetPathRoot((Resolve-Path .).Path)

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $unsafeRunner 'Run-Collectors.ps1') `
            -ConfigPath .\config -OutputPath $filesystemRoot -CleanOutput 2>$null
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
    if ($exitCode -eq 0) { throw "Unsafe cleanup path was accepted" }
}

Test-Case "Collector runner propagates collector failures" {
    $runnerRoot = Join-Path $OutputRoot 'failing-runner'
    New-Item -ItemType Directory -Path $runnerRoot -Force | Out-Null
    Copy-Item .\collectors\Run-Collectors.ps1 $runnerRoot
    Set-Content -Path (Join-Path $runnerRoot 'Collect-IntentionalFailure.ps1') -Value @'
param(
    [string] $ConfigPath,
    [string] $OutputPath
)
exit 7
'@ -Encoding UTF8

    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $runnerRoot 'Run-Collectors.ps1') -ConfigPath .\config -OutputPath (Join-Path $runnerRoot 'output') 2>$null
    if ($LASTEXITCODE -ne 1) { throw "Expected runner exit code 1, got $LASTEXITCODE" }
}

Test-Case "Collector runner forwards redaction to supported collectors" {
    $redactionRunner = Join-Path $OutputRoot 'redaction-runner'
    New-Item -ItemType Directory -Path $redactionRunner -Force | Out-Null
    Copy-Item .\collectors\Run-Collectors.ps1 $redactionRunner
    Set-Content -Path (Join-Path $redactionRunner 'Collect-RedactionProbe.ps1') -Value @'
param(
    [string] $ConfigPath,
    [string] $OutputPath,
    [switch] $RedactPaths
)
if (-not $RedactPaths) {
    exit 8
}
New-Item -Path (Join-Path $OutputPath 'raw') -ItemType Directory -Force | Out-Null
Set-Content -Path (Join-Path (Join-Path $OutputPath 'raw') 'redaction.json') -Value '{"redacted":true}'
exit 0
'@ -Encoding UTF8

    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $redactionRunner 'Run-Collectors.ps1') `
        -ConfigPath .\config -OutputPath (Join-Path $redactionRunner 'output') -RedactPaths 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Expected redaction-capable probe to pass, got $LASTEXITCODE" }
    Assert-FileExists (Join-Path (Join-Path (Join-Path $redactionRunner 'output') 'raw') 'redaction.json')
}

Test-Case "Collector JSON output is atomic and valid" {
    . .\collectors\CollectorHelpers.ps1
    $atomicOutput = Join-Path $OutputRoot 'atomic-output\raw\probe.json'
    Write-JsonOutput -Data @([pscustomobject]@{ server = 'localhost'; value = 'complete' }) -Path $atomicOutput

    $rows = @(ConvertFrom-Json -InputObject (Get-Content -LiteralPath $atomicOutput -Raw))
    if ($rows.Count -ne 1 -or $rows[0].value -ne 'complete') {
        throw "Atomic collector output was not valid JSON"
    }

    $tempFiles = @(Get-ChildItem -LiteralPath (Split-Path -Path $atomicOutput -Parent) -Force -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like '*.tmp' -or $_.Name -like '*.bak' })
    if ($tempFiles.Count -gt 0) {
        throw "Atomic collector output left temporary files behind"
    }
}

Test-Case "Scheduled wrapper reuses six-digit report folders" {
    $scheduledRoot = Join-Path (Resolve-Path $OutputRoot).Path 'scheduled-wrapper'
    $scheduledCollectors = Join-Path $scheduledRoot 'collectors'
    $scheduledReports = Join-Path $scheduledRoot 'reports'
    New-Item -ItemType Directory -Path $scheduledCollectors, $scheduledReports -Force | Out-Null

    $previousFolder = Join-Path $scheduledReports '2026-07-15_123456'
    New-Item -ItemType Directory -Path (Join-Path $previousFolder 'raw') -Force | Out-Null
    Set-Content -Path (Join-Path $previousFolder 'index.html') -Value '<html></html>'
    Copy-Item .\collectors\Run-ScheduledSnapshot.ps1 (Join-Path $scheduledCollectors 'Run-ScheduledSnapshot.ps1')
    Set-Content -Path (Join-Path $scheduledCollectors 'Run-Collectors.ps1') -Value @'
param(
    [string] $ConfigPath,
    [string] $OutputPath
)
New-Item -Path (Join-Path $OutputPath 'raw') -ItemType Directory -Force | Out-Null
Set-Content -Path (Join-Path (Join-Path $OutputPath 'raw') 'sample.json') -Value '{}'
exit 0
'@ -Encoding UTF8

    $reportArgsPath = Join-Path $scheduledRoot 'report-args.txt'
    $env:SCHEDULED_TEST_ARGS = $reportArgsPath
    Set-Content -Path (Join-Path $scheduledRoot 'Fake-Report.ps1') -Value @'
[System.IO.File]::WriteAllText($env:SCHEDULED_TEST_ARGS, ($args -join '|'))
exit 0
'@ -Encoding UTF8
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scheduledCollectors 'Run-ScheduledSnapshot.ps1') `
            -OutputRoot $scheduledReports -ReportExecutablePath (Join-Path $scheduledRoot 'Fake-Report.ps1') 2>$null
        if ($LASTEXITCODE -ne 0) { throw "Scheduled wrapper failed with exit code $LASTEXITCODE" }
    }
    finally {
        Remove-Item Env:SCHEDULED_TEST_ARGS -ErrorAction SilentlyContinue
    }

    Assert-FileExists $reportArgsPath
    $reportArgs = Get-Content $reportArgsPath -Raw
    if ($reportArgs -notmatch [regex]::Escape("--previous|$previousFolder")) {
        throw "Scheduled wrapper did not pass the six-digit previous report folder: $reportArgs"
    }

    Set-Content -Path (Join-Path $scheduledRoot 'Failing-Report.ps1') -Value 'exit 9' -Encoding UTF8
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $scheduledCollectors 'Run-ScheduledSnapshot.ps1') `
            -OutputRoot $scheduledReports -ReportExecutablePath (Join-Path $scheduledRoot 'Failing-Report.ps1') 2>$null
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
    if ($LASTEXITCODE -ne 1) { throw "Expected report failure to exit 1, got $LASTEXITCODE" }
}

$reportNoPrevious = Join-Path $OutputRoot 'report-no-previous'
Test-Case "Engine runs with no previous snapshot" {
    dotnet run --project .\src\OtSnapshotReporter -- --input $collectorRun --config .\config --output $reportNoPrevious | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine failed with no previous snapshot" }
}

Test-Case "Engine flags configured servers with no snapshot data" {
    $coverageConfig = Join-Path $OutputRoot 'coverage-config'
    $coverageReport = Join-Path $OutputRoot 'coverage-report'
    New-Item -ItemType Directory -Path $coverageConfig -Force | Out-Null
    Copy-Item .\config\* -Destination $coverageConfig -Recurse -Force
    Set-Content -Path (Join-Path $coverageConfig 'servers.json') -Value '{"servers":[{"name":"localhost","roles":["snapshot-host"]},{"name":"missing-server","roles":[]}]}' -Encoding UTF8

    dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\demo --config $coverageConfig --output $coverageReport | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine failed while checking configured server coverage" }

    $latest = Get-LatestReport $coverageReport
    $csv = Get-Content (Join-Path $latest 'exceptions.csv') -Raw
    Assert-Contains $csv 'collection_errors,missing-server,Snapshot,Critical,No collector data was received for configured server'
}

Test-Case "Report index exists and is non-trivial" {
    $latest = Get-LatestReport $reportNoPrevious
    $index = Join-Path $latest 'index.html'
    Assert-FileExists $index
    $html = Get-Content $index -Raw
    if ($html.Length -lt 500) { throw "index.html too small: $($html.Length)" }
}

Test-Case "Report contains per-server summary and changes section" {
    $latest = Get-LatestReport $reportNoPrevious
    $html = Get-Content (Join-Path $latest 'index.html') -Raw
    Assert-Contains $html 'Per-Server Summary'
    Assert-Contains $html 'Changes Since Last Snapshot'
}

Test-Case "Report contains all module links" {
    $latest = Get-LatestReport $reportNoPrevious
    $html = Get-Content (Join-Path $latest 'index.html') -Raw
    foreach ($link in @('software_matrix.html', 'odbc_oledb_inventory.html', 'services.html', 'scheduled_tasks.html', 'reboots.html', 'disk_space.html', 'event_log_summary.html', 'file_shares.html', 'backup_freshness.html', 'odbc_dsn_tests.html', 'certificates.html', 'errors.html')) {
        Assert-Contains $html $link
    }
}

Test-Case "Exceptions CSV has expected header" {
    $latest = Get-LatestReport $reportNoPrevious
    $firstLine = Get-Content (Join-Path $latest 'exceptions.csv') -First 1
    if ($firstLine -ne 'Module,Server,Subject,Severity,Message') {
        throw "Unexpected CSV header: $firstLine"
    }
}

Test-Case "Summary.json exists and is valid" {
    $latest = Get-LatestReport $reportNoPrevious
    $summary = Get-Content (Join-Path $latest 'summary.json') -Raw | ConvertFrom-Json
    if ($null -eq $summary.total) { throw "summary.json missing total" }
    if ($null -eq $summary.counts) { throw "summary.json missing counts" }
}

$reportDrift = Join-Path $OutputRoot 'report-drift'
Test-Case "Engine runs with previous snapshot" {
    dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\drift-current --config .\config --output $reportDrift --previous .\samples\previous | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine failed with previous snapshot" }
}

Test-Case "Drift report detects service changes" {
    $csv = Get-Content (Join-Path (Get-LatestReport $reportDrift) 'exceptions.csv') -Raw
    Assert-Contains $csv 'Service status changed'
}

Test-Case "Drift report detects disk changes" {
    $csv = Get-Content (Join-Path (Get-LatestReport $reportDrift) 'exceptions.csv') -Raw
    Assert-Contains $csv 'Free space dropped'
}

Test-Case "Drift report detects reboot" {
    $csv = Get-Content (Join-Path (Get-LatestReport $reportDrift) 'exceptions.csv') -Raw
    Assert-Contains $csv 'rebooted'
}

Test-Case "Drift report detects event log changes" {
    $csv = Get-Content (Join-Path (Get-LatestReport $reportDrift) 'exceptions.csv') -Raw
    Assert-Contains $csv 'critical events'
}

Test-Case "Drift report detects share changes" {
    $csv = Get-Content (Join-Path (Get-LatestReport $reportDrift) 'exceptions.csv') -Raw
    Assert-Contains $csv 'unreachable'
}

Test-Case "Drift report detects backup staleness" {
    $csv = Get-Content (Join-Path (Get-LatestReport $reportDrift) 'exceptions.csv') -Raw
    Assert-Contains $csv 'hours old'
}

$perServerRun = Join-Path $OutputRoot 'per-server'
Test-Case "Per-server collection runs" {
    .\collectors\Run-Collectors.ps1 -OutputPath $perServerRun -PerServer | Out-Null
    $rawDirs = @(Get-ChildItem -Path $perServerRun -Directory -Recurse | Where-Object { $_.Name -eq 'raw' })
    if ($rawDirs.Count -eq 0) {
        throw "No nested raw folder found for per-server collection"
    }
    $servicesPath = Join-Path $rawDirs[0].FullName 'services.json'
    Assert-FileExists $servicesPath
    $services = @(ConvertFrom-Json -InputObject (Get-Content -LiteralPath $servicesPath -Raw))
    if ($services.Count -eq 0) { throw "Per-server collection emitted no service records" }
    $unexpectedServers = @($services | Where-Object { $_.server -ne $env:COMPUTERNAME })
    if ($unexpectedServers.Count -gt 0) {
        throw "Per-server collection targeted unexpected server(s): $(@($unexpectedServers.server) -join ', ')"
    }
}

Test-Case "Engine merges per-server data" {
    $perServerReport = Join-Path $OutputRoot 'per-server-report'
    dotnet run --project .\src\OtSnapshotReporter -- --input $perServerRun --config .\config --output $perServerReport | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine failed to merge per-server data" }
    $latest = Get-LatestReport $perServerReport
    $mergedServicesPath = Join-Path $latest 'raw\services.json'
    Assert-FileExists $mergedServicesPath
    $mergedServices = @(ConvertFrom-Json -InputObject (Get-Content -LiteralPath $mergedServicesPath -Raw))
    if ($mergedServices.Count -eq 0) { throw "Per-server merge produced no service records" }
    if (@($mergedServices | Where-Object { [string]::IsNullOrWhiteSpace($_.server) }).Count -gt 0) {
        throw "Per-server merge produced a service record without a server"
    }
}

Test-Case "--accept-baseline writes expected configs" {
    $baselineConfig = Join-Path $OutputRoot 'baseline-config'
    $baselineOutput = Join-Path $OutputRoot 'baseline-output'
    New-Item -ItemType Directory -Path $baselineConfig -Force | Out-Null
    Copy-Item -Path .\config\* -Destination $baselineConfig -Recurse -Force
    dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\drift-current --config $baselineConfig --output $baselineOutput --accept-baseline | Out-Null
    $expected = Get-Content (Join-Path $baselineConfig 'expected_services.json') -Raw | ConvertFrom-Json
    if (@($expected.services).Count -eq 0) { throw "Expected services baseline to be populated" }
}

Test-Case "Retention cleanup removes old snapshots" {
    $retentionOutput = Join-Path $OutputRoot 'retention-output'
    $retentionConfig = Join-Path $OutputRoot 'retention-config'
    New-Item -ItemType Directory -Path (Join-Path $retentionOutput '2001-01-01_0000\raw') -Force | Out-Null
    Set-Content -Path (Join-Path $retentionOutput '2001-01-01_0000\index.html') -Value '<html></html>'
    New-Item -ItemType Directory -Path (Join-Path $retentionOutput 'collection_2001-01-01_000000') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $retentionOutput 'collection_2999-01-01_000000') -Force | Out-Null
    New-Item -ItemType Directory -Path $retentionConfig -Force | Out-Null
    Copy-Item -Path .\config\* -Destination $retentionConfig -Recurse -Force
    @{
        disk_free_percent_warning = 15
        disk_free_percent_critical = 10
        task_not_run_hours_warning = 24
        reboot_detection_enabled = $true
        disk_drop_percent_warning = 10
        snapshot_retention_days = 1
    } | ConvertTo-Json | Set-Content -Path (Join-Path $retentionConfig 'thresholds.json') -Encoding UTF8
    dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\demo --config $retentionConfig --output $retentionOutput | Out-Null
    if (Test-Path (Join-Path $retentionOutput '2001-01-01_0000')) {
        throw "Old snapshot was not removed"
    }
    if (Test-Path (Join-Path $retentionOutput 'collection_2001-01-01_000000')) {
        throw "Old collection staging folder was not removed"
    }
    if (-not (Test-Path (Join-Path $retentionOutput 'collection_2999-01-01_000000'))) {
        throw "Future collection staging folder was removed unexpectedly"
    }
}

Test-Case "Retention cleanup does not erase the previous snapshot before diffing" {
    $retentionOutput = Join-Path $OutputRoot 'retention-previous-output'
    $retentionConfig = Join-Path $OutputRoot 'retention-previous-config'
    $oldPrevious = Join-Path $retentionOutput '2001-01-01_0000'
    New-Item -ItemType Directory -Path (Join-Path $oldPrevious 'raw') -Force | Out-Null
    Copy-Item -Path .\samples\previous\raw\* -Destination (Join-Path $oldPrevious 'raw') -Recurse -Force
    New-Item -ItemType Directory -Path $retentionConfig -Force | Out-Null
    Copy-Item -Path .\config\* -Destination $retentionConfig -Recurse -Force
    @{
        disk_free_percent_warning = 15
        disk_free_percent_critical = 10
        task_not_run_hours_warning = 24
        reboot_detection_enabled = $true
        disk_drop_percent_warning = 10
        snapshot_retention_days = 1
    } | ConvertTo-Json | Set-Content -Path (Join-Path $retentionConfig 'thresholds.json') -Encoding UTF8

    dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\drift-current --config $retentionConfig --output $retentionOutput --previous $oldPrevious | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine failed while diffing a retained previous snapshot" }

    $latest = Get-LatestReport $retentionOutput
    $csv = Get-Content (Join-Path $latest 'exceptions.csv') -Raw
    Assert-Contains $csv 'Service status changed from Running to Stopped'
    if (Test-Path $oldPrevious) { throw "Old previous snapshot was not removed by retention" }
}

Test-Case "Engine handles empty input directory" {
    $emptyInput = Join-Path $OutputRoot 'empty-input'
    $emptyReport = Join-Path $OutputRoot 'empty-report'
    $emptyConfig = Join-Path $OutputRoot 'empty-config'
    New-Item -ItemType Directory -Path $emptyInput -Force | Out-Null
    New-Item -ItemType Directory -Path $emptyConfig -Force | Out-Null
    Copy-Item .\config\thresholds.json $emptyConfig
    dotnet run --project .\src\OtSnapshotReporter -- --input $emptyInput --config $emptyConfig --output $emptyReport | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine failed for empty input directory" }
    $latest = Get-LatestReport $emptyReport
    $html = Get-Content (Join-Path $latest 'index.html') -Raw
    Assert-Contains $html 'No issues found'
}

Test-Case "Engine rejects a missing input path" {
    $missingInput = Join-Path $OutputRoot 'missing-input-path'
    $missingReport = Join-Path $OutputRoot 'missing-input-report'
    if (Test-Path $missingInput) { Remove-Item -LiteralPath $missingInput -Recurse -Force }

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = dotnet run --project .\src\OtSnapshotReporter -- --input $missingInput --config .\config --output $missingReport 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    if ($exitCode -ne 1) { throw "Expected missing input to exit 1, got $exitCode" }
    if (($output -join "`n") -notmatch 'Input path does not exist') { throw "Expected missing input error" }
}

Test-Case "Engine rejects a missing config path" {
    $missingConfig = Join-Path $OutputRoot 'missing-config-path'
    $missingReport = Join-Path $OutputRoot 'missing-config-report'
    if (Test-Path $missingConfig) { Remove-Item -LiteralPath $missingConfig -Recurse -Force }

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\demo --config $missingConfig --output $missingReport 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    if ($exitCode -ne 1) { throw "Expected missing config to exit 1, got $exitCode" }
    if (($output -join "`n") -notmatch 'Config path does not exist') { throw "Expected missing config error" }
}

Test-Case "Engine rejects an unusable output path" {
    $blockedOutput = Join-Path $OutputRoot 'output-file'
    Set-Content -LiteralPath $blockedOutput -Value 'not a directory' -Encoding UTF8

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\demo --config .\config --output $blockedOutput 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    if ($exitCode -ne 1) { throw "Expected unusable output path to exit 1, got $exitCode" }
    if (($output -join "`n") -notmatch 'Cannot prepare output path') { throw "Expected unusable output path error" }
}

Test-Case "Engine rejects a missing previous snapshot path" {
    $missingPrevious = Join-Path $OutputRoot 'missing-previous-path'
    $missingReport = Join-Path $OutputRoot 'missing-previous-report'
    if (Test-Path $missingPrevious) { Remove-Item -LiteralPath $missingPrevious -Recurse -Force }

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\demo --config .\config --output $missingReport --previous $missingPrevious 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    if ($exitCode -ne 1) { throw "Expected missing previous snapshot to exit 1, got $exitCode" }
    if (($output -join "`n") -notmatch 'Previous snapshot path does not exist') { throw "Expected missing previous snapshot error" }
}

Test-Case "Engine rejects a corrupt threshold config" {
    $configDir = Join-Path $OutputRoot 'corrupt-threshold-config'
    $reportDir = Join-Path $OutputRoot 'corrupt-threshold-report'
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    Copy-Item .\config\* -Destination $configDir -Recurse -Force
    Set-Content -LiteralPath (Join-Path $configDir 'thresholds.json') -Value 'not json' -Encoding UTF8

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\demo --config $configDir --output $reportDir 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    if ($exitCode -ne 1) { throw "Expected corrupt threshold config to exit 1, got $exitCode" }
    if (($output -join "`n") -notmatch 'Config file is invalid.*thresholds.json') { throw "Expected corrupt threshold config error" }
    if (Test-Path $reportDir) { throw "Corrupt threshold config created report output" }
}

Test-Case "Engine rejects invalid threshold values" {
    $configDir = Join-Path $OutputRoot 'invalid-threshold-values'
    $reportDir = Join-Path $OutputRoot 'invalid-threshold-report'
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    Copy-Item .\config\* -Destination $configDir -Recurse -Force
    @{
        disk_free_percent_warning = 10
        disk_free_percent_critical = 20
        task_not_run_hours_warning = -1
        reboot_detection_enabled = $true
        disk_drop_percent_warning = -1
        snapshot_retention_days = 0
    } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $configDir 'thresholds.json') -Encoding UTF8

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\demo --config $configDir --output $reportDir 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    if ($exitCode -ne 1) { throw "Expected invalid threshold values to exit 1, got $exitCode" }
    if (($output -join "`n") -notmatch 'Invalid threshold configuration') { throw "Expected invalid threshold configuration error" }
    if (Test-Path $reportDir) { throw "Invalid threshold values created report output" }
}

Test-Case "Engine rejects a corrupt servers config" {
    $configDir = Join-Path $OutputRoot 'corrupt-servers-config'
    $reportDir = Join-Path $OutputRoot 'corrupt-servers-report'
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    Copy-Item .\config\* -Destination $configDir -Recurse -Force
    Set-Content -LiteralPath (Join-Path $configDir 'servers.json') -Value 'not json' -Encoding UTF8

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\demo --config $configDir --output $reportDir 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    if ($exitCode -ne 1) { throw "Expected corrupt servers config to exit 1, got $exitCode" }
    if (($output -join "`n") -notmatch 'Config file is invalid.*servers.json') { throw "Expected corrupt servers config error" }
    if (Test-Path $reportDir) { throw "Corrupt servers config created report output" }
}

Test-Case "Engine rejects a corrupt expected config" {
    $configDir = Join-Path $OutputRoot 'corrupt-expected-config'
    $reportDir = Join-Path $OutputRoot 'corrupt-expected-report'
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    Copy-Item .\config\* -Destination $configDir -Recurse -Force
    Set-Content -LiteralPath (Join-Path $configDir 'expected_services.json') -Value 'not json' -Encoding UTF8

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\demo --config $configDir --output $reportDir 2>&1
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    if ($exitCode -ne 1) { throw "Expected corrupt expected config to exit 1, got $exitCode" }
    if (($output -join "`n") -notmatch 'Config file is invalid.*expected_services.json') { throw "Expected corrupt expected config error" }
    if (Test-Path $reportDir) { throw "Corrupt expected config created report output" }
}

Test-Case "Engine handles all raw JSON files missing" {
    $missingRawInput = Join-Path $OutputRoot 'missing-raw-input'
    $missingRawReport = Join-Path $OutputRoot 'missing-raw-report'
    New-Item -ItemType Directory -Path (Join-Path $missingRawInput 'raw') -Force | Out-Null
    dotnet run --project .\src\OtSnapshotReporter -- --input $missingRawInput --config .\config --output $missingRawReport | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine failed for missing raw JSON files" }
    Get-LatestReport $missingRawReport | Out-Null
}

Test-Case "Engine handles corrupt JSON and continues" {
    $corruptInput = Join-Path $OutputRoot 'corrupt-input'
    $corruptReport = Join-Path $OutputRoot 'corrupt-report'
    New-Item -ItemType Directory -Path (Join-Path $corruptInput 'raw') -Force | Out-Null
    Copy-Item -Path .\samples\demo\* -Destination (Join-Path $corruptInput 'raw') -Recurse -Force
    Set-Content -Path (Join-Path $corruptInput 'raw\services.json') -Value 'not json'
    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = dotnet run --project .\src\OtSnapshotReporter -- --input $corruptInput --config .\config --output $corruptReport 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Engine failed for corrupt JSON" }
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }
    if (($output -join "`n") -notmatch 'Warning: Failed to parse') { throw "Expected corrupt JSON warning" }
    Get-LatestReport $corruptReport | Out-Null
}

Test-Case "Engine filters malformed records and continues" {
    $malformedInput = Join-Path $OutputRoot 'malformed-input'
    $malformedReport = Join-Path $OutputRoot 'malformed-report'
    New-Item -ItemType Directory -Path (Join-Path $malformedInput 'raw') -Force | Out-Null
    Copy-Item -Path .\samples\demo\* -Destination (Join-Path $malformedInput 'raw') -Recurse -Force
    $services = New-Object System.Collections.Generic.List[object]
    foreach ($service in (Get-Content (Join-Path $malformedInput 'raw\services.json') -Raw | ConvertFrom-Json)) {
        $services.Add($service)
    }
    $services.Add([pscustomobject]@{ server = 'localhost'; name = ''; displayName = ''; status = ''; startupType = ''; startName = '' })
    $services | ConvertTo-Json | Set-Content -Path (Join-Path $malformedInput 'raw\services.json') -Encoding UTF8

    $oldPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $output = dotnet run --project .\src\OtSnapshotReporter -- --input $malformedInput --config .\config --output $malformedReport 2>&1
        if ($LASTEXITCODE -ne 0) { throw "Engine failed for malformed records" }
    }
    finally {
        $ErrorActionPreference = $oldPreference
    }

    if (($output -join "`n") -notmatch 'Ignored 1 invalid service record') { throw "Expected malformed service warning" }
    Get-LatestReport $malformedReport | Out-Null
}

Test-Case "Engine handles missing expected configs" {
    $configDir = Join-Path $OutputRoot 'minimal-config'
    $reportDir = Join-Path $OutputRoot 'minimal-config-report'
    New-Item -ItemType Directory -Path $configDir -Force | Out-Null
    Copy-Item .\config\servers.json $configDir
    Copy-Item .\config\thresholds.json $configDir
    dotnet run --project .\src\OtSnapshotReporter -- --input .\samples\demo --config $configDir --output $reportDir | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine failed for missing expected configs" }
    Get-LatestReport $reportDir | Out-Null
}

Test-Case "Invoke-PerServer retries timeouts and records final error" {
    . .\collectors\CollectorHelpers.ps1
    $timeoutOut = Join-Path $OutputRoot 'timeout-test'
    $rows = Invoke-PerServer -Servers @('slow') -OutputPath $timeoutOut -ServerTimeoutSeconds 1 -MaxRetries 2 -ScriptBlock {
        param($server)
        Start-Sleep -Seconds 5
        [pscustomobject]@{ server = $server }
    }
    if (@($rows).Count -ne 0) { throw "Timed-out collection returned rows" }
    $errors = Get-Content (Join-Path $timeoutOut 'raw\_errors.json') -Raw | ConvertFrom-Json
    if (-not (@($errors) | Where-Object { $_.error -like '*2 attempts*' -and $_.run })) {
        throw "Timeout error did not include retry count and run stamp"
    }
}

Test-Case "Backup collector bounds recursive file enumeration" {
    $boundedRoot = Join-Path $OutputRoot 'bounded-backup-scan'
    $filesPath = Join-Path $boundedRoot 'files'
    New-Item -ItemType Directory -Path $filesPath -Force | Out-Null
    1..3 | ForEach-Object { Set-Content -LiteralPath (Join-Path $filesPath "file-$_.bak") -Value $_ }

    . .\collectors\CollectorHelpers.ps1
    $scan = Get-BoundedFiles -Path $filesPath -MaxFiles 2
    if (@($scan.Files).Count -ne 2) { throw "Expected bounded scan to retain two files" }
    if (-not $scan.Truncated) { throw "Expected bounded scan to report truncation" }

    $configPath = Join-Path $boundedRoot 'config'
    $collectorOutput = Join-Path $boundedRoot 'collector-output'
    New-Item -ItemType Directory -Path $configPath -Force | Out-Null
    Set-Content -LiteralPath (Join-Path $configPath 'servers.json') -Value '{"servers":[{"name":"localhost","roles":[]}]}' -Encoding UTF8
    @{ paths = @(@{ name = 'BoundedBackup'; path = $filesPath; max_age_hours = 24 }) } |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $configPath 'expected_paths.json') -Encoding UTF8
    .\collectors\Collect-BackupFreshness.ps1 -ConfigPath $configPath -OutputPath $collectorOutput -MaxFiles 2 | Out-Null
    Assert-FileExists (Join-Path $collectorOutput 'raw\backup_freshness.json')
}

Test-Case "RedactPaths hides path-bearing collector fields" {
    $redactRoot = Join-Path (Resolve-Path $OutputRoot).Path 'redact-backup'
    $files = Join-Path $redactRoot 'files'
    $config = Join-Path $redactRoot 'config'
    $out = Join-Path $redactRoot 'out'
    New-Item -ItemType Directory -Path $files, $config, $out -Force | Out-Null
    Set-Content -Path (Join-Path $files 'latest.bak') -Value 'x'
    Set-Content -Path (Join-Path $config 'servers.json') -Value '{"servers":[{"name":"localhost","roles":[]}]}'
    @{ paths = @(@{ name = 'Backup'; path = $files; max_age_hours = 24 }) } | ConvertTo-Json | Set-Content -Path (Join-Path $config 'expected_paths.json')
    .\collectors\Collect-BackupFreshness.ps1 -ConfigPath $config -OutputPath $out -RedactPaths
    $rows = Get-Content (Join-Path $out 'raw\backup_freshness.json') -Raw | ConvertFrom-Json
    if (@($rows).Count -eq 0) { throw "Backup collector emitted no rows" }
    if ($rows.path -ne '[redacted]') { throw "Backup path was not redacted" }
    if ($rows.newestFile -ne '[redacted]') { throw "Backup newestFile was not redacted" }

    $shareConfig = Join-Path $redactRoot 'share-config'
    $shareOut = Join-Path $redactRoot 'share-out'
    New-Item -ItemType Directory -Path $shareConfig, $shareOut -Force | Out-Null
    Set-Content -Path (Join-Path $shareConfig 'servers.json') -Value '{"servers":[{"name":"localhost","roles":[]}]}'
    @{ shares = @(@{ name = 'DemoShare'; path = $files }) } | ConvertTo-Json | Set-Content -Path (Join-Path $shareConfig 'shares.json')
    .\collectors\Collect-FileShareReachability.ps1 -ConfigPath $shareConfig -OutputPath $shareOut -RedactPaths
    $shareRows = Get-Content (Join-Path $shareOut 'raw\file_shares.json') -Raw | ConvertFrom-Json
    if (@($shareRows).Count -eq 0) { throw "File-share collector emitted no rows" }
    if ($shareRows.path -ne '[redacted]') { throw "File-share path was not redacted" }
}

Test-Case "All collector scripts parse without syntax errors" {
    $scripts = @(Get-ChildItem .\collectors -Filter 'Collect-*.ps1')
    foreach ($script in $scripts) {
        $parseLexemes = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile($script.FullName, [ref] $parseLexemes, [ref] $errors) > $null
        if ($errors.Count -gt 0) { throw "$($script.Name): $($errors[0].Message)" }
    }
    if ($scripts.Count -lt 10) { throw "Expected at least 10 collector scripts" }
}

Test-Case "Helper and runner scripts parse without syntax errors" {
    foreach ($script in @('.\collectors\CollectorHelpers.ps1', '.\collectors\Run-Collectors.ps1', '.\collectors\Initialize-ExpectedConfig.ps1', '.\collectors\Register-SnapshotTask.ps1', '.\collectors\Run-ScheduledSnapshot.ps1')) {
        $parseLexemes = $null
        $errors = $null
        [System.Management.Automation.Language.Parser]::ParseFile((Resolve-Path $script).Path, [ref] $parseLexemes, [ref] $errors) > $null
        if ($errors.Count -gt 0) { throw "${script}: $($errors[0].Message)" }
    }
}

$elapsed = (Get-Date) - $startTime
Write-Host ""
Write-Host "=== Hardening Complete ===" -ForegroundColor Cyan
Write-Host "  Passed: $script:passCount / $script:testCount"
Write-Host "  Time:   $($elapsed.TotalSeconds.ToString('F1'))s"
Write-Host ""
Write-Host "ALL TESTS PASSED" -ForegroundColor Green
