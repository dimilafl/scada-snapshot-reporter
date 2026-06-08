# OT Snapshot Reporter â€” GUI Launcher Plan

**Target users:** IT domain administrators running from a support server with connectivity to all target domain machines.  
**Environment:** Windows 10+, air-gapped, .NET-only, no external NuGet.

---

## Goal

A single-window desktop app that wraps the existing CLI tools (PowerShell collectors + C# report engine).  
One-click snapshot, inline severity summary, "Open Report" browser launch, console output pane.  
No code changes to the existing engine or collectors â€” the GUI shell-executes them.

---

## MVP Feature Set

| Feature | Description |
|---|---|
| **Server list editor** | Add/remove servers in `servers.json`. Replaces hand-editing JSON. |
| **Collect + Report button** | Runs `Run-Collectors.ps1` then the published engine `.exe`. Sequential, with progress in console pane. |
| **Inline severity summary** | After engine runs, shows Critical/High/Medium/Low/Info counts in colored badges. No browser needed for a quick check. |
| **View HTML button** | Opens the generated `index.html` in the default browser. |
| **Console output pane** | Captures stdout from collector scripts and engine. Shows per-server status. |
| **Basic settings** | Output path, config path, published engine `.exe` path â€” persisted to a `.settings.json`. |

## Deferred (Phase 2)

- Thresholds editor
- Expected configs editor (services, tasks, software, drivers)
- Task Scheduler enable/disable widget
- "Open previous reports" file browser
- Trends/delta view between two snapshots

---

## UI Layout

```
â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”
â”‚  OT Snapshot Reporter                             â”‚
â”‚â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”‚
â”‚  [Servers]                    â”‚  Paths                   â”‚
â”‚  â”Œâ”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”   â”‚  Config: ........ [..]   â”‚
â”‚  â”‚ SRV01                  â”‚   â”‚  Output: ........ [..]   â”‚
â”‚  â”‚ SRV02                  â”‚   â”‚  Engine: ........ [..]   â”‚
â”‚  â”‚ SRV03                  â”‚   â”‚                          â”‚
â”‚  â”‚ â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€  â”‚   â”‚                          â”‚
â”‚  â”‚ [Add]  [Remove]        â”‚   â”‚                          â”‚
â”‚  â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜   â”‚                          â”‚
â”‚â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”‚
â”‚  Summary:  Critical: 2   High: 7   Medium: 18            â”‚
â”‚            Low: 3   Info: 12                              â”‚
â”‚â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”‚
â”‚  [Collect & Report]    [View Report]                      â”‚
â”‚â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”‚
â”‚  Console:                                                 â”‚
â”‚  > [06:00] Collecting from SRV01... OK (12 services, 8   â”‚
â”‚  > [06:01] Collecting from SRV02... OK (14 services, 5   â”‚
â”‚  > [06:02] Collecting from SRV03... ERROR: RPC server    â”‚
â”‚  > [06:03] Report written to Output\2026-06-03_0600      â”‚
â”‚  > [06:03] Critical: 2  High: 7  Medium: 18              â”‚
â”‚  > [06:03] Open index.html to view full report           â”‚
â”‚â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”‚
â”‚  Status: Ready                                            â”‚
â””â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”˜
```

---

## Architecture

### Phase 1: Shell-exec wrapper (no engine changes)

```
GUI (WinForms)
  â”‚
  â”œâ”€â”€ Shell-exec: powershell.exe Run-Collectors.ps1
  â”‚       â””â”€â”€ reads config/servers.json
  â”‚       â””â”€â”€ writes Output/manual-run/raw/*.json
  â”‚
  â”œâ”€â”€ Shell-exec: OtSnapshotReporter.exe --input Output/manual-run
  â”‚       â””â”€â”€ reads raw/*.json
  â”‚       â””â”€â”€ reads config/expected_*.json, thresholds.json
  â”‚       â””â”€â”€ writes Output/YYYY-MM-DD_HHmm/index.html + exceptions.csv
  â”‚
  â”œâ”€â”€ Parse exceptions.csv for severity counts â†’ inline summary badges
  â”‚
  â””â”€â”€ Process.Start("path/to/index.html") â†’ default browser
```

**Why this order:** Zero refactoring. The GUI is a thin shell. If the GUI breaks, the CLI still works. If the CLI changes, the GUI adapts by updating the command-line args â€” the contract is argv, not code dependencies.

### Phase 2: In-process engine calls (after Review #5 refactoring)

Once `Program.cs` is split into `Models.cs`, `Analyzers.cs`, `ReportWriters.cs` and the engine project is refactored to a class library + thin CLI wrapper, the GUI can reference the library directly:

- Real-time progress per analyzer (not just stdout scraping)
- Severity counts updated as each analyzer completes
- No shell-exec overhead
- Cleaner error handling

But don't wait for this â€” Phase 1 delivers useful GUI immediately.

---

## Technology

**Framework:** WinForms (.NET 8, self-contained publish).  
**Why WinForms:** Ships in-box with .NET 8 Windows Desktop runtime. No XAML, no WPF rendering quirks on Win10. Looks native on operations consoles. Dependency-free.  
**Why not WPF:** Adds XAML complexity, no benefit for a utility tool with one form.  
**Why not a web frontend:** Requires a local HTTP server (violates "no server" constraint) or an embedded browser (CEF/WebView2) which adds 100MB+ to the publish and may not work air-gapped.

---

## Project Structure

```
src/
  OtSnapshotReporter/       (existing CLI engine)
  OtSnapshotGui/            (new WinForms app)
    Program.cs
    MainForm.cs
    MainForm.Designer.cs
    Settings.cs              (JSON settings persistence)
    OtSnapshotGui.csproj    (<Project Sdk="Microsoft.NET.Sdk.WindowsDesktop">, <UseWindowsForms>true</UseWindowsForms>)
```

The GUI project references no NuGet packages. All settings persisted as local JSON via `System.Text.Json`. Shell execution via `System.Diagnostics.Process`.

---

## Settings Contract (`gui-settings.json`)

```json
{
  "config_path": "..\\config",
  "output_root": "..\\Output",
  "collector_script": "..\\collectors\\Run-Collectors.ps1",
  "engine_exe": "..\\src\\OtSnapshotReporter\\bin\\Release\\net8.0\\win-x64\\publish\\OtSnapshotReporter.exe",
  "last_report_path": ""
}
```

Defaults resolve relative to the GUI executable, assuming the repo layout. Overridable in the Paths panel.

---

## Key Behaviors

1. **Collect + Report is a single button, not two.** The user shouldn't need to know about the pipeline. One click = collect then report.

2. **Severity count at a glance.** Parse `exceptions.csv` after engine exits. Show colored badge row (Critical=red, High=orange, Medium=yellow, Low=blue, Info=gray). No need to open the browser unless you want details.

3. **Console output is live, not buffered.** Read process stdout asynchronously so each line appears as it emits. Per-server progress visible in real time.

4. **Missing prerequisites detected early.** On startup, check that `powershell.exe` exists, collector script exists, engine `.exe` exists. Red warning on the status bar if anything is missing.

5. **Server list editor writes directly to `servers.json`.** No intermediate format. The existing collectors read `servers.json` â€” the GUI just provides a friendlier editor for it.

6. **Settings persist automatically.** On close, save `gui-settings.json`. On open, restore last paths and window position.

---

## Build & Deploy

```powershell
dotnet publish .\src\OtSnapshotGui\OtSnapshotGui.csproj -c Release -r win-x64 --self-contained true
```

Same self-contained publish pattern as the engine. Deploy the published folder alongside the existing repo layout. The GUI resolves paths relative to its own location.

---

## Non-Goals

- No real-time dashboard (the tool is snapshot-based, not streaming)
- No authentication / multi-user (single support server, domain admins)
- No remote triggering (GUI runs on the support server; collectors run from there via existing WinRM or local as the per-server model evolves)
- No charting / graphs (Phase 3+)
- No notification system (open the report, read the summary)

---

## Implementation Order

| Step | What | Files | Est. lines |
|---|---|---|---|
| 1 | Project scaffold + MainForm layout | `.csproj`, `Program.cs`, `MainForm.cs` | ~80 |
| 2 | Server list editor (load/save `servers.json`) | `MainForm.cs` | ~60 |
| 3 | Shell-exec Collect + Report (stdout capture) | `MainForm.cs` | ~50 |
| 4 | Parse exceptions.csv â†’ severity badges | `MainForm.cs` | ~30 |
| 5 | View HTML button (`Process.Start`) | `MainForm.cs` | ~10 |
| 6 | Settings persistence (`gui-settings.json`) | `Settings.cs` | ~40 |
| 7 | Status bar / startup checks | `MainForm.cs` | ~20 |
| | **Total MVP** | | **~290** |

All in ~300 lines of WinForms code. No dependency on the engine internals.
