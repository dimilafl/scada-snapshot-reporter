# OT Snapshot Reporter

Read-only Windows-native operational snapshot and drift reporter for industrial SCADA/OT environments.

The tool collects local or remote Windows environment snapshots with PowerShell,
then generates static HTML and CSV reports with a dependency-free C# report engine.

## Safety Disclaimer

This project is a generic read-only OT/SCADA environment snapshot/reporting utility.
It is **not affiliated** with any employer, utility, vendor, or production environment.
**All sample data is synthetic.** Do not commit real collector output or generated reports.

## Current Scope

- Software version matrix
- ODBC/OLE DB driver inventory
- Windows services snapshot
- Scheduled task snapshot
- Unexpected reboot detection
- Disk space checks
- Event log summary
- File share reachability
- Backup/export freshness
- SQL Agent job history
- SSRS subscription ownership and delivery status
- Cross-server correlation for repeated findings
- Maintenance-window suppression
- Certificate expiry tracker
- ODBC DSN connection tests

## Layout

```
config/                 Baseline and threshold JSON files
collectors/             PowerShell collectors
src/OtSnapshotReporter  C# report engine
src/OtSnapshotGui       WinForms GUI launcher
samples/demo/           Synthetic demo sample data (safe for public)
tests/                  Test suite
```

## Prerequisites

- PowerShell 5.1 or later
- .NET 8.0 SDK (development) or .NET 8.0 Runtime (deployment)
- Administrator privileges on the collector machine
- WinRM enabled on remote target servers (TCP 5985/5986), or use per-server local collection

## Quick Start

1. Edit `config\servers.json` with your target server hostnames
2. Run collectors: `powershell -File .\collectors\Run-Collectors.ps1 -OutputPath .\Output\manual-run`
3. Bootstrap expected configs: `.\collectors\Initialize-ExpectedConfig.ps1 -InputPath .\Output\manual-run -ConfigPath .\config`
4. Run the report engine: `dotnet run --project .\src\OtSnapshotReporter -- --input .\Output\manual-run --config .\config --output .\Output\report`
5. Open the newest `index.html` under `.\Output\report`

## Build

```powershell
dotnet build .\src\OtSnapshotReporter\OtSnapshotReporter.csproj
dotnet build .\src\OtSnapshotGui\OtSnapshotGui.csproj
```

## Test

```powershell
dotnet test .\tests\OtSnapshotReporter.Tests\OtSnapshotReporter.Tests.csproj
.\tests\Invoke-SmokeTests.ps1
```

Show command-line options with `dotnet run --project .\src\OtSnapshotReporter -- --help`.
Before committing, run `.\scripts\public_audit.ps1` and
`.\tests\Invoke-ConfigValidation.ps1`. Pull requests also run these checks in GitHub Actions.

## Deployment (Air-Gapped / OT)

```powershell
dotnet publish .\src\OtSnapshotReporter\OtSnapshotReporter.csproj -c Release -r win-x64 --self-contained true
```

Register a daily scheduled task:

```powershell
.\collectors\Register-SnapshotTask.ps1 `
  -ReportExecutablePath C:\OtSnapshotReporter\OtSnapshotReporter.exe `
  -RepositoryRoot C:\scada-snapshot-reporter `
  -OutputRoot \\demo-files-01\reports `
  -DailyAt 06:00
```

## Security

See [SECURITY.md](SECURITY.md). Collectors are read-only. Keep generated reports on
restricted shares. Use `.\collectors\Run-Collectors.ps1 -RedactPaths` when reports are
distributed beyond the operations team.

## License

See [LICENSE](LICENSE).
