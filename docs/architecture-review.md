# Architecture Review â€” OT Snapshot Reporter

Date: 2026-06-03 | Base commit: `a550fe8`

> This is a historical review record. The status notes below describe the current public tree and distinguish resolved findings from remaining design debt.

---

## Historical Findings

### 1. `LoadPreviousSnapshot` excludes Phase 2 modules â€” no diff detection

**Status: Resolved.** Phase 2 records are part of `PreviousSnapshot`, and the report engine runs event-log, file-share, and backup drift comparisons.

`LoadPreviousSnapshot` at `src/OtSnapshotReporter/Program.cs:95-110` hardcodes 6 module filenames (services, disk_space, scheduled_tasks, uptime, software, odbc_oledb). Phase 2 modules (event_log_summary, file_shares, backup_freshness) are loaded at lines 37-39 but never included in `PreviousSnapshot`. The `PreviousSnapshot` record at lines 551-560 also has only 6 properties, missing Phase 2.

Impact: Diff detection for event logs, file share reachability, and backup freshness is silently absent. The diff calls at lines 52-56 have no Phase 2 equivalents. A file share that was reachable last run and unreachable now produces no drift finding.

Fix: Add `EventLogSummaryRecords`, `FileShareRecords`, and `BackupFreshnessRecords` to `PreviousSnapshot`, and add `DiffEventLogs`, `DiffFileShares`, `DiffBackups` analyzer methods.

### 2. `FileShareRecord` and `BackupFreshnessRecord` have no `Server` field

**Status: Resolved.** Both records now carry `Server`, and their collectors run through the per-server collection helper.

`FileShareRecord` at `Program.cs:548` has `(string? Name, string Path, bool Reachable, string? Error, string? CheckedAt)`. `BackupFreshnessRecord` at `Program.cs:549` has `(string? Name, string Path, double? MaxAgeHours, bool Exists, ...)`. Every other record type leads with `string Server`.

Impact: In per-server local collection, you can't tell *which server* tested the share. A `Backup/` folder that exists on Server A might not exist on Server B, but the report shows only the share/path without attribution.

Also: `Collect-FileShareReachability.ps1:14` bypasses `Invoke-PerServer` entirely â€” it runs `Test-Path` locally on the collector machine, while all other collectors iterate `$servers`. This is a design mismatch: the collector doesn't know which server it should be testing from.

Fix: Add `Server` field to both records. Make `Collect-FileShareReachability.ps1` use `Invoke-PerServer` like other collectors. In a per-server model, share reachability must be tested from the target server, not the report engine host.

### 3. `Invoke-PerServer` error records leak into data arrays

**Status: Resolved.** Collection errors are written to `_errors.json`; the engine loads them as dedicated collection-error findings. Per-server merge failures are also recorded there.

`CollectorHelpers.ps1:80-95` catches per-server failures and appends `{server, error}` blobs into the same `$results` array as valid records. `Initialize-ExpectedConfig.ps1` filters these via `Test-HasProperty`, but the C# engine does not â€” it deserializes the entire JSON array as typed records (`ServiceRecord`, etc.). Extra fields are silently ignored by `PropertyNameCaseInsensitive`, but required fields like `Name` end up null, producing confusing findings or null-reference issues in string comparisons.

Impact: One unreachable server produces malformed data. The engine doesn't crash (nulls propagate through string comparisons as empty strings due to `EqualsText`), but produces misleading findings like "Service status changed from  to Stopped."

Fix: `Invoke-PerServer` should write errors to a separate `_errors.json` file. The engine should load and display server connectivity errors in its own section. Minimum fix: filter records post-deserialization where `Server` is non-null and `Name` is non-null.

### 4. `Collect-BackupFreshness.ps1` does unbounded recursive scan

**Status: Resolved.** The collector limits recursion to depth 4 and caps the examined file set at 50,000 entries.

`Collect-BackupFreshness.ps1:25`: `Get-ChildItem -Path $expectedPath.path -File -Recurse` with no depth limit, no file count cap. If `expected_paths.json` points at a root drive or a folder with millions of files, the collector will hang or exhaust memory.

Fix: Add `-Depth 4` or configurable max files limit. A misconfigured path should fail fast, not hang the scheduled task.

---

## Design Debt

### 5. Monolithic `Main` with 3+ hardcoded module lists

**Status: Partially resolved.** The public tree separates models, loading, analysis, diffing, and report rendering. `Program.cs` still coordinates module loading and output explicitly; that is a future refactoring opportunity, not a known missing-feature bug.

`Program.cs` is 748 lines in a single file. Every module appears in 5+ locations:

| Concern | Lines | Module count |
|---|---|---|
| JSON loading | 31-39 | 9 hardcoded filenames |
| Baseline analysis | 43-51 | 9 hardcoded calls |
| Drift detection (diff) | 52-56 | 5 hardcoded calls (missing Phase 2) |
| HTML output | 58-67 | 8 hardcoded page writes (1 matrix, 7 table) |
| Model records | 541-560 | 12 records inline |
| HTML templates | 582-748 | All inline |

Impact: Adding a 10th module requires editing lines at 5+ locations. The compiler can't help because each line is `findings.AddRange(...)` or `HtmlReport.WriteTable(...)` â€” mismatches produce no build error, only silent omission at runtime. This already manifested: Phase 2 diffs are missing (bug #1).

Recommendation: Define a module registry (list of `(collectorFile, recordType, configType, analyzer, differ, htmlWriter)` tuples) that `Main` iterates. Or at minimum extract models to `Models.cs`, analyzers to `Analyzers.cs`, HTML to `ReportWriters.cs`.

### 6. `Run-Collectors.ps1` mirrors the same hardcoded-list problem

**Status: Resolved.** The runner discovers `Collect-*.ps1` files by convention and now propagates collector failures to callers.

`Run-Collectors.ps1:8-16` calls 9 collectors by explicit filename. Adding a collector means editing this file. The smoke test runner at `tests/Invoke-SmokeTests.ps1:75` iterates `Get-ChildItem -Filter *.ps1` for parsing but not for execution â€” it misses collector results.

Recommendation: Either convention-based discovery (`Get-ChildItem Collect-*.ps1 | ForEach-Object { & $_ }`) or a collector manifest file that both the runner and smoke test read.

### 7. No JSON schema validation between collectors and engine

**Status: Partially resolved.** Server coverage is checked against `config/servers.json`, and `Loading.LoadRecords` rejects rows missing required identity fields with an explicit warning. Full field-level schema/version validation remains future design debt.

The collector-to-engine contract is the implicit field names in `[pscustomobject]`. `PropertyNameCaseInsensitive = true` means typos in field names (`server` vs `Server`) are silently accepted but `displayName` renamed to `DisplayName` produces empty cells in HTML. No validation step exists.

Recommendation: Extend the required-field predicates as contracts evolve; ideally, embed a versioned schema hash per collector output.

### 8. `Register-SnapshotTask.ps1` uses interactive user, not SYSTEM

**Status: Resolved.** Scheduled registration uses the local SYSTEM service account with highest run level.

`Register-SnapshotTask.ps1:27`: `-UserId ([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType S4U`. S4U means "run only when user is logged on" â€” if the user is logged off at 06:00, the task doesn't run. This also means the task runs under the caller's credentials, which may lack admin on remote servers or share write permissions. The per-server deployment model needs either SYSTEM (which already has local admin and computer-account network identity) or a dedicated service account with constrained permissions.

Note: This is currently a convenience script for workstation testing. For production OT deployment, SYSTEM or a service account is required.

---

## Per-Server Local Collection Architecture

The current central-host model requires WinRM (port 5985/5986) and admin credentials on every target â€” both blocked in most OT environments per CIS benchmarks. The proposed alternative:

```
Current:  Central Host --[WinRM]--> Server A/B/C  (requires WinRM + admin + RPC ports)
Proposed: Server A --[Task Sched SYSTEM]--> local collect --> \\share\server-a\
          Server B --[Task Sched SYSTEM]--> local collect --> \\share\server-b\
          Central Host --[C# Engine]--> reads merged \\share\ --> report
```

The loader accepts both `server\raw` folders and the scheduled runner's
`server\timestamp\raw` folders. When several timestamped runs exist for one
server, it selects the newest timestamp before merging, so stale runs are not
silently mixed into the report.

### Strengths

- **Eliminates WinRM dependency.** No open inbound port on target servers post-deployment. Only SMB 445 needed for share access (already open for OT file shares).
- **Eliminates admin rights on remote targets.** SYSTEM has full local admin. No domain admin account needed for collection.
- **Eliminates cross-domain Kerberos/NTLM problems.** Each server talks to itself locally + writes via computer account to share. No cross-machine authentication.
- **Eliminates DCOM dynamic RPC port requirements.** `Get-CimInstance` runs locally, no firewall holes for ports 49152-65535.
- **Eliminates "Log on as batch job" requirement.** SYSTEM already has all needed rights.
- **Self-contained publish removes .NET runtime dependency.** Already documented in README and `.csproj`.

### Weaknesses and Mitigations

| Hole | Severity | Mitigation |
|---|---|---|
| **Timing skew across servers.** If clocks diverge, per-server snapshot timestamps differ, making cross-server correlation ambiguous. Engine uses its own clock for report timestamp; per-server timestamps are metadata only. No functional impact on diff since diff compares same-server records across runs. | Low | Accept. Engine timestamp is canonical. |
| **`--previous` semantics require merged data.** Currently `--previous` points at a single dated folder. In per-server, the engine's output folder becomes the canonical previous â€” it contains merged data. Next run's `--previous` points at the last engine output, not per-server data. | Low | Engine output folder is always the previous. No semantic change needed. |
| **Network share as SPOF.** If the share is down, all 9 servers fail simultaneously (current WinRM model: central host could buffer locally). | Medium | Scheduled task writes to local temp first, copies to share. If copy fails, engine can fall back to reading per-server local data if accessible. |
| **SYSTEM account network identity.** `NT AUTHORITY\SYSTEM` authenticates as `DOMAIN\SERVERA$` to network resources. Requires computer account write permissions on share. Works for domain-joined servers; fails for workgroup. | Medium | For workgroup: use domain service account in scheduled task. Narrower scope than current WinRM model (one share write vs admin on all servers). |
| **Deployment touch same as enabling WinRM.** Both require one configuration change per server. Scheduled task has closed port after; WinRM has open port permanently. | Low | Scheduled task is better long-term. |
| **Collector version skew.** If scripts live on share and are updated, servers that start before the update run old version while late-starters run new version. Results in mixed-version snapshots. | Low | Add `collector_version` field to records. Engine flags mixed versions in report. |
| **`FileShareRecord`/`BackupFreshnessRecord` lack `Server` field.** (Bug #2 above.) Must fix before per-server deployment. | High | Add `Server` field to both records, update collectors. |
| **`Invoke-PerServer` error blobs contaminate data.** (Bug #3 above.) In per-server model, independent failures are more common. | High | Filter in engine post-deserialization; log server errors to separate report section. |
| **Phase 2 modules have no diff detection.** (Bug #1 above.) File share that was up last run and is down now produces no drift finding. | High | Add Phase 2 records to `PreviousSnapshot` and implement diff methods. |
| **Unbounded recursion in backup collector.** (Bug #4 above.) Misconfigured path hangs the collector. | Medium | Add depth limit or file count cap. |
| **No "expected servers" vs "actual servers" tracking.** Engine loads whatever data is present. Missing server is not detected. | Medium | **Resolved.** Engine reads `servers.json` and flags configured servers with no snapshot data as a Critical collection finding. |
| **Engine merge step.** Separate `Merge-Snapshots.ps1` would be unnecessary coupling. Engine should discover per-server subdirs and load in memory. | Low | **Resolved.** Engine discovers both direct and timestamped per-server `raw/` folders, selects the newest run per server, and merges in memory. No separate merge tool. |

---

## Priority Matrix

| # | Finding | Severity | Category | Blocks per-server? |
|---|---|---|---|---|
| 1 | Phase 2 modules excluded from `PreviousSnapshot` | High | Correctness | No |
| 2 | `FileShareRecord` / `BackupFreshnessRecord` missing `Server` | High | Correctness | **Yes** |
| 3 | Error records leak into data arrays | High | Correctness | **Yes** |
| 4 | Unbounded `-Recurse` in backup collector | Medium | Correctness | No |
| 5 | Monolithic `Program.cs` with 5+ hardcoded lists | Medium | Design debt | No |
| 6 | `Run-Collectors.ps1` hardcoded list | Medium | Design debt | No |
| 7 | No JSON schema validation | Low | Design debt | No |
| 8 | `Register-SnapshotTask` uses user identity, not SYSTEM | Low | Design debt | **Yes** |
| 9 | Missing configured server coverage | Resolved | Correctness | No |

Items 2, 3, and 8 are prerequisites for per-server local collection. Server coverage is now implemented; field-level schema validation and the remaining design-debt items can be addressed independently.
