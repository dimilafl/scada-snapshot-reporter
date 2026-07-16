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

var runStamp = DateTime.Now.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture);
var reportRoot = Path.Combine(options.OutputPath, runStamp);
var rawOutput = Path.Combine(reportRoot, "raw");

Directory.CreateDirectory(reportRoot);
Directory.CreateDirectory(rawOutput);

var json = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
};

var rawRoot = Loading.ResolveRawRoot(options.InputPath, options.OutputPath);
Loading.CopyRawInputs(rawRoot, rawOutput);

var thresholds = Loading.LoadJson<Thresholds>(Path.Combine(options.ConfigPath, "thresholds.json"), json) ?? new Thresholds();
var configuredServers = Loading.LoadJson<ServersConfig>(Path.Combine(options.ConfigPath, "servers.json"), json) ?? new ServersConfig();
var expectedServices = Loading.LoadJson<ExpectedServicesConfig>(Path.Combine(options.ConfigPath, "expected_services.json"), json) ?? new ExpectedServicesConfig();
var expectedTasks = Loading.LoadJson<ExpectedTasksConfig>(Path.Combine(options.ConfigPath, "expected_tasks.json"), json) ?? new ExpectedTasksConfig();
var expectedSoftware = Loading.LoadJson<ExpectedSoftwareConfig>(Path.Combine(options.ConfigPath, "expected_software.json"), json) ?? new ExpectedSoftwareConfig();
var expectedDrivers = Loading.LoadJson<ExpectedDriversConfig>(Path.Combine(options.ConfigPath, "expected_drivers.json"), json) ?? new ExpectedDriversConfig();
var maintenanceWindows = Loading.LoadJson<MaintenanceWindowsConfig>(Path.Combine(options.ConfigPath, "maintenance_windows.json"), json) ?? new MaintenanceWindowsConfig();

var services = (Loading.LoadJson<List<ServiceRecord>>(Path.Combine(rawRoot, "services.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Name)).ToList();
var disks = (Loading.LoadJson<List<DiskRecord>>(Path.Combine(rawRoot, "disk_space.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server)).ToList();
var tasks = (Loading.LoadJson<List<TaskRecord>>(Path.Combine(rawRoot, "scheduled_tasks.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.TaskName)).ToList();
var uptimes = (Loading.LoadJson<List<UptimeRecord>>(Path.Combine(rawRoot, "uptime.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server)).ToList();
var software = (Loading.LoadJson<List<SoftwareRecord>>(Path.Combine(rawRoot, "software.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Name)).ToList();
var drivers = (Loading.LoadJson<List<DriverRecord>>(Path.Combine(rawRoot, "odbc_oledb.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Name)).ToList();
var eventLogs = (Loading.LoadJson<List<EventLogSummaryRecord>>(Path.Combine(rawRoot, "event_log_summary.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server)).ToList();
var fileShares = (Loading.LoadJson<List<FileShareRecord>>(Path.Combine(rawRoot, "file_shares.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server)).ToList();
var backups = (Loading.LoadJson<List<BackupFreshnessRecord>>(Path.Combine(rawRoot, "backup_freshness.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server)).ToList();
var odbcDsns = (Loading.LoadJson<List<OdbcDsnRecord>>(Path.Combine(rawRoot, "odbc_dsn_tests.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server)).ToList();
var certificates = (Loading.LoadJson<List<CertificateRecord>>(Path.Combine(rawRoot, "certificates.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server)).ToList();
var sqlAgentJobs = (Loading.LoadJson<List<SqlAgentJobRecord>>(Path.Combine(rawRoot, "sql_agent_jobs.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server)).ToList();
var ssrsSubscriptions = (Loading.LoadJson<List<SsrsSubscriptionRecord>>(Path.Combine(rawRoot, "ssrs_subscriptions.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server)).ToList();
var collectionErrors = (Loading.LoadJson<List<ErrorRecord>>(Path.Combine(rawRoot, "_errors.json"), json) ?? []).Where(x => !string.IsNullOrWhiteSpace(x.Server)).ToList();
var previous = Loading.LoadPreviousSnapshot(options.PreviousPath, json);
Writing.CleanupOldSnapshots(options.OutputPath, thresholds.SnapshotRetentionDays);
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
