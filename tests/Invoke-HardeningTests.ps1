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

$reportNoPrevious = Join-Path $OutputRoot 'report-no-previous'
Test-Case "Engine runs with no previous snapshot" {
    dotnet run --project .\src\OtSnapshotReporter -- --input $collectorRun --config .\config --output $reportNoPrevious | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine failed with no previous snapshot" }
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
}

Test-Case "Engine merges per-server data" {
    $perServerReport = Join-Path $OutputRoot 'per-server-report'
    dotnet run --project .\src\OtSnapshotReporter -- --input $perServerRun --config .\config --output $perServerReport | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Engine failed to merge per-server data" }
    Get-LatestReport $perServerReport | Out-Null
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
    if ($rows.newestFile -ne '[redacted]') { throw "Backup newestFile was not redacted" }
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
