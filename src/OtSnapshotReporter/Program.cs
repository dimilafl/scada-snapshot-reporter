using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using OtSnapshotReporter.Analysis;
using OtSnapshotReporter.Diff;
using OtSnapshotReporter.Infrastructure;
using OtSnapshotReporter.Models;
using OtSnapshotReporter.Reporting;

AppOptions options;
try
{
    options = AppOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    Console.Error.WriteLine(AppOptions.Usage);
    return 2;
}

if (options.HelpRequested)
{
    Console.WriteLine(AppOptions.Usage);
    return 0;
}

if (!Directory.Exists(options.InputPath))
{
    Console.Error.WriteLine($"Error: Input path does not exist: {options.InputPath}");
    return 1;
}

if (!Directory.Exists(options.ConfigPath))
{
    Console.Error.WriteLine($"Error: Config path does not exist: {options.ConfigPath}");
    return 1;
}

if (!string.IsNullOrWhiteSpace(options.PreviousPath) && !Directory.Exists(options.PreviousPath))
{
    Console.Error.WriteLine($"Error: Previous snapshot path does not exist: {options.PreviousPath}");
    return 1;
}

var json = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
};

T LoadOptionalConfig<T>(string path, T fallback) where T : class
{
    if (!File.Exists(path))
    {
        return fallback;
    }

    return Loading.LoadJson<T>(path, json) ?? throw new InvalidDataException($"Config file is invalid: {path}");
}

var thresholdsPath = Path.Combine(options.ConfigPath, "thresholds.json");
var thresholds = new Thresholds();
if (File.Exists(thresholdsPath))
{
    thresholds = Loading.LoadJson<Thresholds>(thresholdsPath, json);
    if (thresholds is null)
    {
        Console.Error.WriteLine($"Error: Config file is invalid: {thresholdsPath}");
        return 1;
    }
}

var thresholdErrors = new List<string>();
if (thresholds.DiskFreePercentCritical < 0 || thresholds.DiskFreePercentWarning < 0)
{
    thresholdErrors.Add("disk free thresholds must be non-negative");
}
if (thresholds.DiskFreePercentCritical > thresholds.DiskFreePercentWarning)
{
    thresholdErrors.Add("critical disk threshold must be less than or equal to warning threshold");
}
if (thresholds.TaskNotRunHoursWarning < 0)
{
    thresholdErrors.Add("task_not_run_hours_warning must be non-negative");
}
if (thresholds.DiskDropPercentWarning < 0)
{
    thresholdErrors.Add("disk_drop_percent_warning must be non-negative");
}
if (thresholds.SnapshotRetentionDays < 1)
{
    thresholdErrors.Add("snapshot_retention_days must be at least 1");
}
if (thresholdErrors.Count > 0)
{
    Console.Error.WriteLine($"Error: Invalid threshold configuration in {thresholdsPath}: {string.Join("; ", thresholdErrors)}");
    return 1;
}

var serversPath = Path.Combine(options.ConfigPath, "servers.json");
var configuredServers = new ServersConfig();
if (File.Exists(serversPath))
{
    configuredServers = Loading.LoadJson<ServersConfig>(serversPath, json);
    if (configuredServers is null)
    {
        Console.Error.WriteLine($"Error: Config file is invalid: {serversPath}");
        return 1;
    }
}

ExpectedServicesConfig expectedServices;
ExpectedTasksConfig expectedTasks;
ExpectedSoftwareConfig expectedSoftware;
ExpectedDriversConfig expectedDrivers;
MaintenanceWindowsConfig maintenanceWindows;
try
{
    expectedServices = LoadOptionalConfig(Path.Combine(options.ConfigPath, "expected_services.json"), new ExpectedServicesConfig());
    expectedTasks = LoadOptionalConfig(Path.Combine(options.ConfigPath, "expected_tasks.json"), new ExpectedTasksConfig());
    expectedSoftware = LoadOptionalConfig(Path.Combine(options.ConfigPath, "expected_software.json"), new ExpectedSoftwareConfig());
    expectedDrivers = LoadOptionalConfig(Path.Combine(options.ConfigPath, "expected_drivers.json"), new ExpectedDriversConfig());
    maintenanceWindows = LoadOptionalConfig(Path.Combine(options.ConfigPath, "maintenance_windows.json"), new MaintenanceWindowsConfig());
}
catch (InvalidDataException ex)
{
    Console.Error.WriteLine($"Error: {ex.Message}");
    return 1;
}

try
{
    Directory.CreateDirectory(options.OutputPath);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"Error: Cannot prepare output path: {options.OutputPath}. {ex.Message}");
    return 1;
}

string reportRoot;
string rawOutput;
try
{
    reportRoot = Writing.CreateAvailableReportRoot(options.OutputPath, DateTime.Now);
    rawOutput = Path.Combine(reportRoot, "raw");
    Directory.CreateDirectory(rawOutput);
}
catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
{
    Console.Error.WriteLine($"Error: Cannot prepare report folder under {options.OutputPath}. {ex.Message}");
    return 1;
}

var rawRoot = Loading.ResolveRawRoot(options.InputPath, options.OutputPath);
Loading.CopyRawInputs(rawRoot, rawOutput);

var services = Loading.LoadRecords<ServiceRecord>(Path.Combine(rawRoot, "services.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Name), "service");
var disks = Loading.LoadRecords<DiskRecord>(Path.Combine(rawRoot, "disk_space.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Drive), "disk");
var tasks = Loading.LoadRecords<TaskRecord>(Path.Combine(rawRoot, "scheduled_tasks.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.TaskName), "scheduled task");
var uptimes = Loading.LoadRecords<UptimeRecord>(Path.Combine(rawRoot, "uptime.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.LastBootTime), "uptime");
var software = Loading.LoadRecords<SoftwareRecord>(Path.Combine(rawRoot, "software.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Name), "software");
var drivers = Loading.LoadRecords<DriverRecord>(Path.Combine(rawRoot, "odbc_oledb.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Type) && !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Architecture), "driver");
var eventLogs = Loading.LoadRecords<EventLogSummaryRecord>(Path.Combine(rawRoot, "event_log_summary.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.LogName) && !string.IsNullOrWhiteSpace(x.Source), "event log");
var fileShares = Loading.LoadRecords<FileShareRecord>(Path.Combine(rawRoot, "file_shares.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Path), "file-share");
var backups = Loading.LoadRecords<BackupFreshnessRecord>(Path.Combine(rawRoot, "backup_freshness.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Path), "backup");
var odbcDsns = Loading.LoadRecords<OdbcDsnRecord>(Path.Combine(rawRoot, "odbc_dsn_tests.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.DsnName), "ODBC DSN");
var certificates = Loading.LoadRecords<CertificateRecord>(Path.Combine(rawRoot, "certificates.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Subject), "certificate");
var sqlAgentJobs = Loading.LoadRecords<SqlAgentJobRecord>(Path.Combine(rawRoot, "sql_agent_jobs.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.JobName), "SQL Agent job");
var ssrsSubscriptions = Loading.LoadRecords<SsrsSubscriptionRecord>(Path.Combine(rawRoot, "ssrs_subscriptions.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.ReportPath), "SSRS subscription");
var collectionErrors = Loading.LoadRecords<ErrorRecord>(Path.Combine(rawRoot, "_errors.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Error), "collection error");
var previous = Loading.LoadPreviousSnapshot(options.PreviousPath, json);
Writing.CleanupOldSnapshots(options.OutputPath, thresholds.SnapshotRetentionDays);
Writing.CleanupOldCollectionStaging(options.OutputPath, thresholds.SnapshotRetentionDays);
var observedServers = services.Select(x => x.Server)
    .Concat(disks.Select(x => x.Server))
    .Concat(tasks.Select(x => x.Server))
    .Concat(uptimes.Select(x => x.Server))
    .Concat(software.Select(x => x.Server))
    .Concat(drivers.Select(x => x.Server))
    .Concat(eventLogs.Select(x => x.Server))
    .Concat(fileShares.Select(x => x.Server))
    .Concat(backups.Select(x => x.Server))
    .Concat(odbcDsns.Select(x => x.Server))
    .Concat(certificates.Select(x => x.Server))
    .Concat(sqlAgentJobs.Select(x => x.Server))
    .Concat(ssrsSubscriptions.Select(x => x.Server))
    .Concat(collectionErrors.Select(x => x.Server));

var findings = new List<Finding>();
findings.AddRange(Analyzers.AnalyzeMissingServers(configuredServers, observedServers));
findings.AddRange(Analyzers.AnalyzeServices(services, expectedServices));
findings.AddRange(Analyzers.AnalyzeDisks(disks, thresholds));
findings.AddRange(Analyzers.AnalyzeTasks(tasks, expectedTasks, thresholds));
findings.AddRange(Analyzers.AnalyzeUptime(uptimes, thresholds, previous));
findings.AddRange(Analyzers.AnalyzeSoftware(software, expectedSoftware));
findings.AddRange(Analyzers.AnalyzeDrivers(drivers, expectedDrivers));
findings.AddRange(Analyzers.AnalyzeCollectionErrors(collectionErrors));
findings.AddRange(Analyzers.AnalyzeEventLogs(eventLogs));
findings.AddRange(Analyzers.AnalyzeFileShares(fileShares));
findings.AddRange(Analyzers.AnalyzeBackups(backups));
findings.AddRange(Analyzers.AnalyzeOdbcDsns(odbcDsns));
findings.AddRange(Analyzers.AnalyzeCertificates(certificates));
findings.AddRange(Analyzers.AnalyzeSqlAgentJobs(sqlAgentJobs));
findings.AddRange(Analyzers.AnalyzeSsrsSubscriptions(ssrsSubscriptions));
findings.AddRange(DiffEngine.DiffServices(services, previous.Services));
findings.AddRange(DiffEngine.DiffDisks(disks, previous.Disks, thresholds));
findings.AddRange(DiffEngine.DiffTasks(tasks, previous.Tasks));
findings.AddRange(DiffEngine.DiffSoftware(software, previous.Software));
findings.AddRange(DiffEngine.DiffDrivers(drivers, previous.Drivers));
findings.AddRange(DiffEngine.DiffEventLogs(eventLogs, previous.EventLogs));
findings.AddRange(DiffEngine.DiffFileShares(fileShares, previous.FileShares));
findings.AddRange(DiffEngine.DiffBackups(backups, previous.Backups));
findings = FindingPostProcessors.AddCorrelationFindings(findings);
findings = FindingPostProcessors.ApplyMaintenanceWindows(findings, maintenanceWindows, DateTime.Now);

if (options.AcceptBaseline)
{
    Writing.WriteBaselineConfigs(options.ConfigPath, services, tasks, software, drivers);
    Console.WriteLine("Baseline configs updated from current snapshot.");
    return 0;
}

if (findings.Count == 0)
{
    Console.WriteLine("No findings detected.");
}

var pages = Loading.GetModuleDescriptors().Select(x => (x.HtmlFile, x.DisplayName)).Append(("exceptions.csv", "Exceptions CSV")).ToList();
HtmlReport.WriteIndex(Path.Combine(reportRoot, "index.html"), findings, pages);
HtmlReport.WriteTable(Path.Combine(reportRoot, "services.html"), "Windows Services", services, findings, "services");
HtmlReport.WriteTable(Path.Combine(reportRoot, "disk_space.html"), "Disk Space", disks, findings, "disk");
HtmlReport.WriteTable(Path.Combine(reportRoot, "scheduled_tasks.html"), "Scheduled Tasks", tasks, findings, "scheduled_tasks");
HtmlReport.WriteTable(Path.Combine(reportRoot, "reboots.html"), "Unexpected Reboot Detection", uptimes, findings, "uptime");
HtmlReport.WriteSoftwareMatrix(Path.Combine(reportRoot, "software_matrix.html"), software, expectedSoftware, findings);
HtmlReport.WriteDriverMatrix(Path.Combine(reportRoot, "odbc_oledb_inventory.html"), drivers, expectedDrivers, findings);
HtmlReport.WriteTable(Path.Combine(reportRoot, "errors.html"), "Collection Errors", collectionErrors, findings, "collection_errors");
HtmlReport.WriteTable(Path.Combine(reportRoot, "event_log_summary.html"), "Event Log Summary", eventLogs, findings, "event_logs");
HtmlReport.WriteTable(Path.Combine(reportRoot, "file_shares.html"), "File Share Reachability", fileShares, findings, "file_shares");
HtmlReport.WriteTable(Path.Combine(reportRoot, "backup_freshness.html"), "Backup Freshness", backups, findings, "backups");
HtmlReport.WriteTable(Path.Combine(reportRoot, "odbc_dsn_tests.html"), "ODBC DSN Tests", odbcDsns, findings, "odbc_dsns");
HtmlReport.WriteTable(Path.Combine(reportRoot, "certificates.html"), "Certificate Expiry", certificates, findings, "certificates");
HtmlReport.WriteTable(Path.Combine(reportRoot, "sql_agent_jobs.html"), "SQL Agent Jobs", sqlAgentJobs, findings, "sql_agent_jobs");
HtmlReport.WriteTable(Path.Combine(reportRoot, "ssrs_subscriptions.html"), "SSRS Subscriptions", ssrsSubscriptions, findings, "ssrs_subscriptions");
HtmlReport.WriteFindingsPage(Path.Combine(reportRoot, "correlation.html"), "Cross-Server Correlation", findings, "correlation");
CsvReport.WriteFindings(Path.Combine(reportRoot, "exceptions.csv"), findings);
Writing.WriteSummaryJson(Path.Combine(reportRoot, "summary.json"), reportRoot, findings);

Console.WriteLine($"Report written to {reportRoot}");
return 0;
