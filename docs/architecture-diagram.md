# Architecture Overview - OT Snapshot Reporter

## Data Flow

```text
target servers (1..N)
  Collect-*.ps1 scripts
    -> raw/*.json
      -> C# report engine
        -> analyzers compare current data to expected config
        -> diff engine compares current data to previous snapshot
        -> static output: index.html, module pages, exceptions.csv, summary.json
```

## Component Map

| Component | Language | File(s) | Purpose |
|-----------|----------|---------|---------|
| Collectors | PowerShell 5.1 | `collectors/Collect-*.ps1` | Gather snapshots from Windows servers |
| Collector helpers | PowerShell 5.1 | `collectors/CollectorHelpers.ps1` | Server config, JSON output, per-server dispatch, timeout handling |
| Run orchestration | PowerShell 5.1 | `collectors/Run-Collectors.ps1` | Discovers and runs all collectors |
| Bootstrap | PowerShell 5.1 | `collectors/Initialize-ExpectedConfig.ps1` | Seeds baseline from a known-good snapshot |
| Scheduled task | PowerShell 5.1 | `collectors/Register-SnapshotTask.ps1` | Registers daily collection |
| Report engine | C# .NET 8 | `src/OtSnapshotReporter/` | Loads JSON, analyzes, computes drift, writes reports |
| GUI launcher | C# WinForms | `src/OtSnapshotGui/` | One-click collect/report workflow |
| Smoke tests | PowerShell 5.1 | `tests/Invoke-SmokeTests.ps1` | End-to-end integration tests |
| Unit tests | C# xUnit | `tests/OtSnapshotReporter.Tests/` | Isolated analyzer, diff, CSV, and helper tests |

## Deployment Models

### Central Collection

One support workstation runs collectors against all configured servers. This requires administrative rights on targets plus WinRM or remote WMI/CIM access.

### Per-Server Collection

Each server runs `Run-Collectors.ps1 -PerServer` locally and writes to a shared output directory. This avoids inbound WinRM to target servers, and the report engine merges per-server `raw` folders in memory-backed temporary input during report generation.

## Collector Schema Reference

| Raw file | Primary fields | Notes |
|----------|----------------|-------|
| `services.json` | `Server`, `Name`, `DisplayName`, `Status`, `StartupType`, `StartName` | Compared to `expected_services.json` |
| `disk_space.json` | `Server`, `Drive`, `TotalGb`, `FreeGb`, `FreePercent` | Uses warning and critical thresholds |
| `scheduled_tasks.json` | `Server`, `TaskPath`, `TaskName`, `Enabled`, `LastRunTime`, `LastTaskResult`, `RunAs`, `Action` | Compared to `expected_tasks.json` |
| `uptime.json` | `Server`, `LastBootTime`, `UptimeHours` | Previous snapshot detects reboots |
| `software.json` | `Server`, `Name`, `Version`, `Publisher`, `InstallLocation` | Compared to `expected_software.json` |
| `odbc_oledb.json` | `Server`, `Type`, `Name`, `Version`, `Architecture`, `InstallPath`, `LastModified` | Compared to `expected_drivers.json` |
| `event_log_summary.json` | `Server`, `LogName`, `Source`, `Level`, `Count`, `NewestTime`, `NewestEventId`, `WindowHours` | `Level` uses Windows numeric levels, where 1 is critical and 2 is error |
| `file_shares.json` | `Server`, `Name`, `Path`, `Reachable`, `Error`, `CheckedAt` | Unreachable shares produce findings |
| `backup_freshness.json` | `Server`, `Name`, `Path`, `MaxAgeHours`, `Exists`, `NewestFile`, `NewestWriteTime`, `AgeHours`, `Error` | Missing, empty, and stale paths produce findings |
| `odbc_dsn_tests.json` | `Server`, `DsnName`, `DriverName`, `Type`, `Architecture`, `ServerTarget`, `Database`, `ConnectionPassed` | Failed DSN tests produce findings |
| `certificates.json` | `Server`, `Subject`, `Issuer`, `Thumbprint`, `NotBefore`, `NotAfter`, `DaysUntilExpiry`, `Store` | Expired and expiring certificates produce findings |
| `sql_agent_jobs.json` | `Server`, `Instance`, `JobName`, `Enabled`, `LastRunDate`, `LastRunStatus`, `LastRunMessage`, `JobOwner` | Failed, retrying, cancelled, or stale enabled jobs produce findings |
| `ssrs_subscriptions.json` | `Server`, `Instance`, `ReportPath`, `Owner`, `OwnerExists`, `LastStatus`, `LastRunTime`, `Enabled` | Missing owners and failed delivery status produce findings |
| `_errors.json` | `server`, `error`, `run` | Fresh per collector run; records timeout and exception failures |

## Post-Analysis Passes

| Pass | Input | Output |
|------|-------|--------|
| Maintenance suppression | `config/maintenance_windows.json` plus current findings | Matching findings are downgraded to `Info` and annotated |
| Cross-server correlation | Current findings | Adds `correlation` findings when three or more servers share the same module, subject, and message |
