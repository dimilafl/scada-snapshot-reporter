# Codex Work Spec â€” OT Snapshot Reporter

Base commit: `454a842` (`Add GUI launcher and collector error handling`)
Working directory: `C:\dev\Scada-Programming`

---

## Batch A â€” Data Contract Fixes + Diff Completion

### A1: Add `Server` field to FileShareRecord and BackupFreshnessRecord

These two record types lack a `Server` field, which blocks per-server deployment.

**File 1: `src/OtSnapshotReporter/Program.cs` line 548**

Change:
```csharp
sealed record FileShareRecord(string? Name, string Path, bool Reachable, string? Error, string? CheckedAt);
```
To:
```csharp
sealed record FileShareRecord(string Server, string? Name, string Path, bool Reachable, string? Error, string? CheckedAt);
```

**File 1: `src/OtSnapshotReporter/Program.cs` line 549**

Change:
```csharp
sealed record BackupFreshnessRecord(string? Name, string Path, double? MaxAgeHours, bool Exists, string? NewestFile, string? NewestWriteTime, double? AgeHours, string? Error);
```
To:
```csharp
sealed record BackupFreshnessRecord(string Server, string? Name, string Path, double? MaxAgeHours, bool Exists, string? NewestFile, string? NewestWriteTime, double? AgeHours, string? Error);
```

**File 2: `collectors/Collect-FileShareReachability.ps1`**

Currently runs `Test-Path` from the central host without `Invoke-PerServer`. Rewrite to use `Invoke-PerServer` like every other collector. Each target server tests share reachability from itself.

Replace the entire `$data =` block (lines 14-32) with:
```powershell
$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    Invoke-ServerScript -Server $server -ScriptBlock {
        $shares = @()
        $sharesFile = Join-Path $using:ConfigPath 'shares.json'
        if (Test-Path $sharesFile) {
            $shares = @((Get-Content -Path $sharesFile -Raw | ConvertFrom-Json).shares)
        }

        foreach ($share in $shares) {
            $reachable = $false
            $errorText = $null
            try {
                $reachable = Test-Path -Path $share.path -ErrorAction Stop
            }
            catch {
                $errorText = $_.Exception.Message
            }

            [pscustomobject]@{
                server = $env:COMPUTERNAME
                name = $share.name
                path = $share.path
                reachable = $reachable
                error = $errorText
                checkedAt = (Get-Date).ToString("s")
            }
        }
    }
}
```

Note: `$using:ConfigPath` passes the local variable into the remote script block. Remove the old `$sharesFile` load at the top of the script (lines 8-12) since it's now inside the script block.

**File 3: `collectors/Collect-BackupFreshness.ps1`**

Add `server = $env:COMPUTERNAME` to the output `[pscustomobject]` at line 41. The collector already doesn't use `Invoke-PerServer` â€” it should. Wrap the collection in `Invoke-PerServer` like the file share collector above, or add the server field directly if paths are file-share-based and only testable from one location.

Minimum fix: add to line 41:
```powershell
server = $env:COMPUTERNAME
```

Full fix: wrap in `Invoke-PerServer` with `-ScriptBlock` that loads `expected_paths.json` and tests each path. Same pattern as FileShareReachability.

---

### A2: Phase 2 diffs â€” add EventLog, FileShare, BackupFreshness to PreviousSnapshot

**File: `src/OtSnapshotReporter/Program.cs`**

**Step 1: Load Phase 2 data in `LoadPreviousSnapshot` (lines 95-110)**

Currently loads 6 module files. Add 3 more after the driver line:

```csharp
static PreviousSnapshot LoadPreviousSnapshot(string? path, JsonSerializerOptions json)
{
    if (string.IsNullOrWhiteSpace(path))
    {
        return PreviousSnapshot.Empty;
    }

    var raw = Path.Combine(path, "raw");
    return new PreviousSnapshot(
        LoadJson<List<ServiceRecord>>(Path.Combine(raw, "services.json"), json) ?? [],
        LoadJson<List<DiskRecord>>(Path.Combine(raw, "disk_space.json"), json) ?? [],
        LoadJson<List<TaskRecord>>(Path.Combine(raw, "scheduled_tasks.json"), json) ?? [],
        LoadJson<List<UptimeRecord>>(Path.Combine(raw, "uptime.json"), json) ?? [],
        LoadJson<List<SoftwareRecord>>(Path.Combine(raw, "software.json"), json) ?? [],
        LoadJson<List<DriverRecord>>(Path.Combine(raw, "odbc_oledb.json"), json) ?? [],
        LoadJson<List<EventLogSummaryRecord>>(Path.Combine(raw, "event_log_summary.json"), json) ?? [],
        LoadJson<List<FileShareRecord>>(Path.Combine(raw, "file_shares.json"), json) ?? [],
        LoadJson<List<BackupFreshnessRecord>>(Path.Combine(raw, "backup_freshness.json"), json) ?? []);
}
```

**Step 2: Update `PreviousSnapshot` record (lines 551-560)**

Add 3 properties:
```csharp
sealed record PreviousSnapshot(
    IReadOnlyCollection<ServiceRecord> Services,
    IReadOnlyCollection<DiskRecord> Disks,
    IReadOnlyCollection<TaskRecord> Tasks,
    IReadOnlyCollection<UptimeRecord> Uptimes,
    IReadOnlyCollection<SoftwareRecord> Software,
    IReadOnlyCollection<DriverRecord> Drivers,
    IReadOnlyCollection<EventLogSummaryRecord> EventLogs,
    IReadOnlyCollection<FileShareRecord> FileShares,
    IReadOnlyCollection<BackupFreshnessRecord> Backups)
{
    public static PreviousSnapshot Empty { get; } = new([], [], [], [], [], [], [], [], []);
}
```

**Step 3: Add diff calls in Main (after line 56)**

```csharp
findings.AddRange(DiffEventLogs(eventLogs, previous.EventLogs));
findings.AddRange(DiffFileShares(fileShares, previous.FileShares));
findings.AddRange(DiffBackups(backups, previous.Backups));
```

**Step 4: Implement `DiffEventLogs` method (new, ~20 lines)**

```csharp
static IEnumerable<Finding> DiffEventLogs(IEnumerable<EventLogSummaryRecord> records, IReadOnlyCollection<EventLogSummaryRecord> previous)
{
    if (previous.Count == 0)
        yield break;

    var currentByKey = records.ToDictionary(x => Key(x.Server, $"{x.LogName}|{x.Source}"), StringComparer.OrdinalIgnoreCase);
    var previousByKey = previous.ToDictionary(x => Key(x.Server, $"{x.LogName}|{x.Source}"), StringComparer.OrdinalIgnoreCase);

    foreach (var (key, old) in previousByKey)
    {
        if (!currentByKey.TryGetValue(key, out var current))
        {
            yield return Finding.Create("event_logs", old.Server, $"{old.LogName}/{old.Source}", Severity.Low, "Event source no longer present");
            continue;
        }

        if (current.Count > old.Count * 2 && current.Count > 10)
        {
            yield return Finding.Create("event_logs", current.Server, $"{current.LogName}/{current.Source}", Severity.Medium, $"Event count rose from {old.Count} to {current.Count}");
        }
    }

    foreach (var (key, current) in currentByKey)
    {
        if (!previousByKey.ContainsKey(key) && current.Count > 5)
        {
            yield return Finding.Create("event_logs", current.Server, $"{current.LogName}/{current.Source}", Severity.Low, $"New event source with {current.Count} events");
        }
    }
}
```

**Step 5: Implement `DiffFileShares` method (new, ~15 lines)**

```csharp
static IEnumerable<Finding> DiffFileShares(IEnumerable<FileShareRecord> records, IReadOnlyCollection<FileShareRecord> previous)
{
    if (previous.Count == 0)
        yield break;

    var previousByKey = previous.ToDictionary(x => Key(x.Server, x.Path), StringComparer.OrdinalIgnoreCase);
    foreach (var share in records)
    {
        if (!previousByKey.TryGetValue(Key(share.Server, share.Path), out var old))
            continue;

        if (old.Reachable && !share.Reachable)
        {
            yield return Finding.Create("file_shares", share.Server, share.Name ?? share.Path, Severity.High, "Share was reachable previously, now unreachable");
        }
        else if (!old.Reachable && share.Reachable)
        {
            yield return Finding.Create("file_shares", share.Server, share.Name ?? share.Path, Severity.Medium, "Share recovered since previous snapshot");
        }
    }
}
```

**Step 6: Implement `DiffBackups` method (new, ~18 lines)**

```csharp
static IEnumerable<Finding> DiffBackups(IEnumerable<BackupFreshnessRecord> records, IReadOnlyCollection<BackupFreshnessRecord> previous)
{
    if (previous.Count == 0)
        yield break;

    var previousByKey = previous.ToDictionary(x => Key(x.Server, x.Path), StringComparer.OrdinalIgnoreCase);
    foreach (var backup in records)
    {
        if (!previousByKey.TryGetValue(Key(backup.Server, backup.Path), out var old))
            continue;

        if (old.Exists && !backup.Exists)
        {
            yield return Finding.Create("backups", backup.Server, backup.Name ?? backup.Path, Severity.High, "Backup/export path disappeared since previous snapshot");
        }

        if (backup.AgeHours.HasValue && old.AgeHours.HasValue && backup.AgeHours.Value > old.AgeHours.Value * 3)
        {
            yield return Finding.Create("backups", backup.Server, backup.Name ?? backup.Path, Severity.Medium, $"Newest file age increased from {old.AgeHours:0.#}h to {backup.AgeHours:0.#}h");
        }
    }
}
```

---

### A3: Register-SnapshotTask SYSTEM principal

**File: `collectors/Register-SnapshotTask.ps1` line 27**

Change:
```powershell
$principal = New-ScheduledTaskPrincipal -UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType S4U -RunLevel Highest
```
To:
```powershell
# SYSTEM runs with local admin + computer account network identity.
# For share-write access in a workgroup, change to a domain service account.
$principal = New-ScheduledTaskPrincipal -UserId "NT AUTHORITY\SYSTEM" -LogonType ServiceAccount -RunLevel Highest
```

---

## Batch B â€” Engine Quality + Config

### B1: Snapshot retention cleanup

**File 1: `config/thresholds.json`**

Add:
```json
"snapshot_retention_days": 90
```

**File 2: `src/OtSnapshotReporter/Program.cs` â€” `Thresholds` class (line ~491)**

Add property:
```csharp
[JsonPropertyName("snapshot_retention_days")] public int SnapshotRetentionDays { get; set; } = 90;
```

**File 2: `src/OtSnapshotReporter/Program.cs` â€” Main method (after line 13, before any analysis)**

Add cleanup call:
```csharp
CleanupOldSnapshots(options.OutputPath, thresholds.SnapshotRetentionDays);
```

**File 2: `src/OtSnapshotReporter/Program.cs` â€” new method (~15 lines)**

```csharp
static void CleanupOldSnapshots(string outputPath, int retentionDays)
{
    if (!Directory.Exists(outputPath) || retentionDays <= 0)
        return;

    var cutoff = DateTime.Now.AddDays(-1 * retentionDays);
    foreach (var dir in Directory.GetDirectories(outputPath))
    {
        var name = Path.GetFileName(dir);
        if (name.Length >= 15 && DateTime.TryParseExact(name.Substring(0, 16), "yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp))
        {
            if (timestamp < cutoff)
            {
                Directory.Delete(dir, recursive: true);
                Console.WriteLine($"Cleaned up old snapshot: {name}");
            }
        }
    }
}
```

---

### B2: `--accept-baseline` flag

**File: `src/OtSnapshotReporter/Program.cs` â€” `AppOptions` record + Parse method (lines ~446-480)**

Add `AcceptBaseline` property:
```csharp
sealed record AppOptions(string InputPath, string ConfigPath, string OutputPath, string? PreviousPath, bool AcceptBaseline)
{
    public static AppOptions Parse(string[] args)
    {
        // ... existing code ...
        var acceptBaseline = false;

        for (var i = 0; i < args.Length; i++)
        {
            // ... existing cases ...
            switch (args[i])
            {
                // ... existing cases ...
                case "--accept-baseline":
                    acceptBaseline = true;
                    break;
            }
        }

        return new AppOptions(input, config, output, previous, acceptBaseline);
    }
}
```

**File: `src/OtSnapshotReporter/Program.cs` â€” Main method (after all findings are collected, before HTML output)**

Add:
```csharp
if (options.AcceptBaseline)
{
    WriteBaselineConfigs(options.ConfigPath, services, tasks, software, drivers);
    Console.WriteLine("Baseline configs updated from current snapshot.");
    return;
}
```

**File: `src/OtSnapshotReporter/Program.cs` â€” new method (~35 lines)**

```csharp
static void WriteBaselineConfigs(string configPath, IReadOnlyCollection<ServiceRecord> services, IReadOnlyCollection<TaskRecord> tasks, IReadOnlyCollection<SoftwareRecord> software, IReadOnlyCollection<DriverRecord> drivers)
{
    Directory.CreateDirectory(configPath);
    var json = new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };

    var servicesConfig = new ExpectedServicesConfig(services.OrderBy(x => x.Server).ThenBy(x => x.Name).Select(x =>
        new ExpectedService(x.Server, x.Name, x.Status, x.StartupType, "Critical")).ToList());
    File.WriteAllText(Path.Combine(configPath, "expected_services.json"), JsonSerializer.Serialize(servicesConfig, json));

    var tasksConfig = new ExpectedTasksConfig(tasks.OrderBy(x => x.Server).ThenBy(x => x.TaskPath).ThenBy(x => x.TaskName).Select(x =>
        new ExpectedTask(x.Server, x.TaskPath, x.TaskName, x.Enabled)).ToList());
    File.WriteAllText(Path.Combine(configPath, "expected_tasks.json"), JsonSerializer.Serialize(tasksConfig, json));

    var softwareConfig = new ExpectedSoftwareConfig(software.Where(x => !string.IsNullOrWhiteSpace(x.Version)).OrderBy(x => x.Server).ThenBy(x => x.Name).Select(x =>
        new ExpectedSoftware(x.Server, x.Name, x.Version ?? "")).ToList());
    File.WriteAllText(Path.Combine(configPath, "expected_software.json"), JsonSerializer.Serialize(softwareConfig, json));

    var driversConfig = new ExpectedDriversConfig(drivers.Where(x => !string.IsNullOrWhiteSpace(x.Version)).OrderBy(x => x.Server).ThenBy(x => x.Type).ThenBy(x => x.Name).ThenBy(x => x.Architecture).Select(x =>
        new ExpectedDriver(x.Server, x.Type, x.Name, x.Architecture, x.Version ?? "")).ToList());
    File.WriteAllText(Path.Combine(configPath, "expected_drivers.json"), JsonSerializer.Serialize(driversConfig, json));
}
```

Note: Need `using System.Text.Json;` and `JsonNamingPolicy.SnakeCaseLower` for the `JsonPropertyName` attributes to work. The `[JsonPropertyName("...")]` attributes on the expected config records already map snake_case. Alternatively, don't use naming policy and just rely on the existing attributes â€” simply serialize with default options and the attributes will handle the mapping.

To keep it simple and avoid breaking the existing deserialization attributes, use the same `JsonSerializerOptions` as the rest of the engine:

```csharp
var json = new JsonSerializerOptions { WriteIndented = true };
```

The `[JsonPropertyName]` attributes on `ExpectedService`, `ExpectedTask`, etc. already map the C# PascalCase properties to snake_case JSON keys.

**File: `collectors/Initialize-ExpectedConfig.ps1`**

Add note in help text or comment that `--accept-baseline` is the preferred method. Optionally add a switch to call the engine with the flag:

```powershell
if ($AcceptBaseline) {
    $engineExe = Join-Path $RepositoryRoot 'src\OtSnapshotReporter\bin\Release\net8.0\win-x64\publish\OtSnapshotReporter.exe'
    & $engineExe --input $InputPath --config $ConfigPath --accept-baseline
}
```

---

### B3: Event log collector â€” configurable logs

**New file: `config/event_log_config.json`**
```json
{
  "logs": [
    { "name": "Application", "include_warnings": false, "max_events": 500 },
    { "name": "System", "include_warnings": false, "max_events": 500 }
  ],
  "window_hours": 24
}
```

**File: `collectors/Collect-EventLogSummary.ps1`**

Replace hardcoded `@('Application', 'System')` (line 18) with reading from the config file:

```powershell
$logConfigFile = Join-Path $ConfigPath 'event_log_config.json'
$logNames = @('Application', 'System')
$includeWarnings = $false
$windowHours = $Hours      # fall back to CLI param

if (Test-Path $logConfigFile) {
    $logConfig = Get-Content -Path $logConfigFile -Raw | ConvertFrom-Json
    $logNames = @($logConfig.logs | ForEach-Object { $_.name })
    $windowHours = if ($logConfig.window_hours) { $logConfig.window_hours } else { $Hours }
}
```

Then use `$logNames` in the `Get-WinEvent -FilterHashtable` call (line 18), and `$windowHours` instead of `$Hours` in the time calculation (line 17). If a log config entry has `include_warnings: true`, add level 3 to the Level filter.

---

### B4: Expand smoke tests

**File: `tests/Invoke-SmokeTests.ps1`**

Add assertions after the existing drift checks (around line 43):

```powershell
# Phase 2 module pages exist
foreach ($page in @('event_log_summary.html', 'file_shares.html', 'backup_freshness.html')) {
    if (-not (Test-Path (Join-Path $latest.FullName $page))) {
        throw "Phase 2 page missing: $page"
    }
}

# Backup freshness: sample fixtures should produce age-threshold finding
$sampleOutput = Join-Path $RepositoryRoot 'Output\test-backup-stale'
if (Test-Path $sampleOutput) { Remove-Item -LiteralPath $sampleOutput -Recurse -Force }

# Create sample config with stale threshold
$backupConfig = Join-Path $RepositoryRoot 'Output\test-backup-stale-config'
if (Test-Path $backupConfig) { Remove-Item -LiteralPath $backupConfig -Recurse -Force }
New-Item -ItemType Directory -Path $backupConfig -Force | Out-Null
Copy-Item -Path (Join-Path $RepositoryRoot 'config\*') -Destination $backupConfig -Recurse -Force

# Create sample backup data with stale files
$sampleRaw = Join-Path $RepositoryRoot 'Output\test-backup-stale-raw'
if (Test-Path $sampleRaw) { Remove-Item -LiteralPath $sampleRaw -Recurse -Force }
New-Item -ItemType Directory -Path $sampleRaw -Force | Out-Null

# Copy existing sample and add backup fixture
Copy-Item -Path (Join-Path $RepositoryRoot 'samples\drift-current\raw\*') -Destination $sampleRaw -Recurse -Force

$sampleBackupData = @(
    @{host='localhost'; name='StaleExport'; path='C:\Exports'; maxAgeHours=1.0; exists=$true; newestFile='C:\Exports\export.csv'; newestWriteTime=(Get-Date).AddHours(-5).ToString('s'); ageHours=5.0; error=$null}
) | ConvertTo-Json
Set-Content -Path (Join-Path $sampleRaw 'backup_freshness.json') -Value $sampleBackupData -Encoding UTF8

$staleConfig = @{paths=@(
    @{name='StaleExport'; path='C:\Exports'; max_age_hours=1.0}
)} | ConvertTo-Json
Set-Content -Path (Join-Path $backupConfig 'expected_paths.json') -Value $staleConfig -Encoding UTF8

dotnet run --project .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -- --input $sampleRaw --config $backupConfig --output $sampleOutput | Write-Host
$staleLatest = Get-ChildItem -Path $sampleOutput -Directory | Sort-Object Name -Descending | Select-Object -First 1
$staleCsv = Get-Content -Path (Join-Path $staleLatest.FullName 'exceptions.csv') -Raw
Assert-Contains -Content $staleCsv -Expected 'hours old; threshold is'

# File share unreachable fixture
$sampleShareRaw = Join-Path $RepositoryRoot 'Output\test-share-raw'
if (Test-Path $sampleShareRaw) { Remove-Item -LiteralPath $sampleShareRaw -Recurse -Force }
New-Item -ItemType Directory -Path $sampleShareRaw -Force | Out-Null
Copy-Item -Path (Join-Path $RepositoryRoot 'samples\drift-current\raw\*') -Destination $sampleShareRaw -Recurse -Force

$sampleShareData = @(
    @{host='localhost'; name='DeadShare'; path='\\dead\share'; reachable=$false; error='Network path not found'; checkedAt=(Get-Date).ToString('s')}
) | ConvertTo-Json
Set-Content -Path (Join-Path $sampleShareRaw 'file_shares.json') -Value $sampleShareData -Encoding UTF8

$shareConfig = @{shares=@(
    @{name='DeadShare'; path='\\dead\share'}
)} | ConvertTo-Json
Set-Content -Path (Join-Path $backupConfig 'shares.json') -Value $shareConfig -Encoding UTF8

dotnet run --project .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -- --input $sampleShareRaw --config $backupConfig --output $sampleOutput | Write-Host
$shareLatest = Get-ChildItem -Path $sampleOutput -Directory | Sort-Object Name -Descending | Select-Object -First 1
$shareCsv = Get-Content -Path (Join-Path $shareLatest.FullName 'exceptions.csv') -Raw
Assert-Contains -Content $shareCsv -Expected 'Share is unreachable'

# Error page exists when _errors.json present (already tested by existing drift â€” verify the HTML file is generated)
# The errors page should exist even if empty
```

Note: Keep the existing test structure. The new assertions should go AFTER the existing Phase 2 page checks and BEFORE the cleanup section. The `$latest` variable already holds the drift fixture output folder.

---

### B5: Sample report output

Run this once manually and commit the results:

```powershell
# Create expected-output sample from drift fixtures
$sampleOut = '.\samples\expected-output'
if (Test-Path $sampleOut) { Remove-Item -LiteralPath $sampleOut -Recurse -Force }

dotnet run --project .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -- `
    --input .\samples\drift-current `
    --previous .\samples\previous `
    --config .\config `
    --output $sampleOut

# Open the generated index.html in a browser to verify visually
# Then commit the whole dated folder:
git add samples/expected-output/
git commit -m "Add sample expected report output for reference"
```

This gives reviewers a static example of what the tool produces without needing to run anything.

---

## Batch C â€” Architecture (after A+B land)

### C1: Per-server local collection

**Goal:** Remove WinRM dependency. Each target server runs collectors locally, writes to a network share subdirectory. The engine discovers per-server directories and merges in memory.

**File: `collectors/Run-Collectors.ps1`**

Add `-TargetServers` parameter:

```powershell
param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run",
    [string[]] $TargetServers = @()
)
```

If `$TargetServers` is non-empty, each collector is called with `-TargetServers $TargetServers` and the collector should override `servers.json` with the explicit list. Or simpler: export `$env:TARGET_SERVER` and have `Get-ConfiguredServers` respect it.

Simpler approach â€” add a `-PerServer` mode:

```powershell
param(
    [string] $ConfigPath = ".\config",
    [string] $OutputRoot = ".\Output",
    [switch] $PerServer
)
```

In per-server mode, each server collects only itself and writes to `$OutputRoot\$env:COMPUTERNAME\$timestamp\raw\`. The central engine runs afterward reading `$OutputRoot\*\latest\raw\`.

Full spec deferred. Minimum for this batch: add the `-PerServer` param and per-server output path logic.

**File: `src/OtSnapshotReporter/Program.cs` â€” `LoadJson` expansion**

Add a method that discovers per-server input directories:

```csharp
static string? ResolveRawRoot(string inputPath)
{
    // Single-directory mode (existing)
    var single = Path.Combine(inputPath, "raw");
    if (Directory.Exists(single))
        return single;

    // Per-server mode: look for server subdirectories
    var serverDirs = Directory.GetDirectories(inputPath)
        .Select(d => new { Path = d, Raw = Path.Combine(d, "raw") })
        .Where(x => Directory.Exists(x.Raw))
        .ToList();

    if (serverDirs.Count == 0)
        return null;

    // TODO: merge per-server raw/ into temp directory, or load into memory
    // For now, return first server's raw/ as fallback
    return serverDirs[0].Raw;
}
```

Full per-server merge logic deferred to a follow-up. This batch creates the infrastructure.

---

### C2: ODBC DSN connection test collector

**New file: `collectors/Collect-OdbcDsnTests.ps1`**

Read ODBC DSNs from registry, test connectivity, report pass/fail.

```powershell
param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run"
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    Invoke-ServerScript -Server $server -ScriptBlock {
        $results = New-Object System.Collections.Generic.List[object]

        # System DSNs (64-bit)
        $systemDsn64 = Get-ItemProperty -Path 'HKLM:\Software\ODBC\ODBC.INI\ODBC SourceNames' -ErrorAction SilentlyContinue
        if ($systemDsn64) {
            $systemDsn64.PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' } | ForEach-Object {
                $dsnName = $_.Name
                $driverName = $_.Value
                $dsnKey = Get-ItemProperty -Path "HKLM:\Software\ODBC\ODBC.INI\$dsnName" -ErrorAction SilentlyContinue
                $connectionSetting = if ($dsnKey) { "DSN=$dsnName" } else { $null }
                $passed = Test-OdbcConnection -ConnectionSetting $connectionSetting
                $results.Add([pscustomobject]@{
                    server = $env:COMPUTERNAME
                    dsnName = $dsnName
                    driverName = $driverName
                    type = 'System'
                    architecture = '64-bit'
                    server_target = if ($dsnKey.Server) { $dsnKey.Server } else { $null }
                    database = if ($dsnKey.Database) { $dsnKey.Database } else { $null }
                    connectionPassed = $passed
                })
            }
        }

        # Repeat for 32-bit: HKLM:\Software\WOW6432Node\ODBC\ODBC.INI\ODBC SourceNames
        # Same pattern, architecture = '32-bit'

        return $results
    }
}

function Test-OdbcConnection {
    param([string]$ConnectionSetting)
    if (-not $ConnectionSetting) { return $false }
    try {
        $conn = New-Object System.Data.Odbc.OdbcConnection($ConnectionSetting)
        $conn.ConnectionTimeout = 5
        $conn.Open()
        $conn.Close()
        return $true
    }
    catch {
        return $false
    }
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\odbc_dsn_tests.json')
```

**File: `src/OtSnapshotReporter/Program.cs`**

Add model record:
```csharp
sealed record OdbcDsnRecord(string Server, string DsnName, string DriverName, string Type, string Architecture, string? ServerTarget, string? Database, bool ConnectionPassed);
```

Add analyzer (~15 lines):
```csharp
static IEnumerable<Finding> AnalyzeOdbcDsns(IEnumerable<OdbcDsnRecord> records)
{
    foreach (var dsn in records)
    {
        if (!dsn.ConnectionPassed)
        {
            yield return Finding.Create("odbc_dsns", dsn.Server, dsn.DsnName, Severity.High, $"DSN connection test failed (driver: {dsn.DriverName})");
        }
    }
}
```

Wire into Main: load `odbc_dsn_tests.json`, call analyzer, add HTML page.

**File: `collectors/Run-Collectors.ps1`**

Add to discovery-based runner (already done via glob pattern â€” `Collect-*.ps1` picks it up automatically).

---

### C3: Certificate expiry tracker

**New file: `collectors/Collect-Certificates.ps1`**

```powershell
param(
    [string] $ConfigPath = ".\config",
    [string] $OutputPath = ".\Output\manual-run"
)

. "$PSScriptRoot\CollectorHelpers.ps1"

$servers = Get-ConfiguredServers -ConfigPath $ConfigPath
$data = Invoke-PerServer -Servers $servers -OutputPath $OutputPath -ScriptBlock {
    param($server)

    Invoke-ServerScript -Server $server -ScriptBlock {
        $results = New-Object System.Collections.Generic.List[object]
        $stores = @('Cert:\LocalMachine\My', 'Cert:\LocalMachine\Root', 'Cert:\LocalMachine\CA')

        foreach ($storePath in $stores) {
            Get-ChildItem -Path $storePath -ErrorAction SilentlyContinue | ForEach-Object {
                $daysUntilExpiry = ($_.NotAfter - (Get-Date)).Days
                $results.Add([pscustomobject]@{
                    server = $env:COMPUTERNAME
                    subject = $_.Subject
                    issuer = $_.Issuer
                    thumbprint = $_.Thumbprint
                    notBefore = $_.NotBefore.ToString("s")
                    notAfter = $_.NotAfter.ToString("s")
                    daysUntilExpiry = $daysUntilExpiry
                    store = $storePath
                })
            }
        }

        return $results
    }
}

Write-JsonOutput -Data $data -Path (Join-Path $OutputPath 'raw\certificates.json')
```

**File: `src/OtSnapshotReporter/Program.cs`**

Add model:
```csharp
sealed record CertificateRecord(string Server, string Subject, string Issuer, string Thumbprint, string NotBefore, string NotAfter, int DaysUntilExpiry, string Store);
```

Add analyzer:
```csharp
static IEnumerable<Finding> AnalyzeCertificates(IEnumerable<CertificateRecord> records)
{
    foreach (var cert in records)
    {
        if (cert.DaysUntilExpiry < 0)
        {
            yield return Finding.Create("certificates", cert.Server, cert.Subject, Severity.Critical, $"Certificate expired {Math.Abs(cert.DaysUntilExpiry)} days ago");
        }
        else if (cert.DaysUntilExpiry <= 30)
        {
            yield return Finding.Create("certificates", cert.Server, cert.Subject, Severity.High, $"Certificate expires in {cert.DaysUntilExpiry} days");
        }
        else if (cert.DaysUntilExpiry <= 60)
        {
            yield return Finding.Create("certificates", cert.Server, cert.Subject, Severity.Medium, $"Certificate expires in {cert.DaysUntilExpiry} days");
        }
        else if (cert.DaysUntilExpiry <= 90)
        {
            yield return Finding.Create("certificates", cert.Server, cert.Subject, Severity.Low, $"Certificate expires in {cert.DaysUntilExpiry} days");
        }
    }
}
```

Wire into Main: load, analyze, HTML table page, navigation link. Works automatically via `Run-Collectors.ps1` discovery.

---

## Validation

After each batch lands, run:
```powershell
.\tests\Invoke-SmokeTests.ps1
```

Must pass with 0 failures. After all batches:
```powershell
dotnet build .\src\OtSnapshotReporter\OtSnapshotReporter.csproj
dotnet build .\src\OtSnapshotGui\OtSnapshotGui.csproj
```
Both must compile with 0 warnings, 0 errors.

---

## Commit Strategy

Commit each batch separately so regressions are bisectable:

```
Batch A: "Add Server field to FileShare/BackupFreshness records, Phase 2 diffs, SYSTEM principal"
Batch B: "Add snapshot retention, --accept-baseline, configurable event logs, expanded smoke tests"
Batch C: "Add per-server collection mode, ODBC DSN tests, certificate expiry tracker"
```

---

## Files NOT to Touch

- `collectors/CollectorHelpers.ps1` (error handling already done in 454a842)
- `collectors/Collect-SoftwareInventory.ps1` (no changes needed)
- `collectors/Collect-Services.ps1` (no changes needed)
- `collectors/Collect-ScheduledTasks.ps1` (no changes needed)
- `collectors/Collect-Uptime.ps1` (no changes needed)
- `collectors/Collect-DiskSpace.ps1` (no changes needed)
- `collectors/Collect-OdbcOleDbDrivers.ps1` (no changes needed)
- `src/OtSnapshotGui/*` (no changes needed â€” separate workstream)
