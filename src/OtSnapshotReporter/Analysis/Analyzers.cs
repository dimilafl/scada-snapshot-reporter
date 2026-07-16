using OtSnapshotReporter.Infrastructure;
using OtSnapshotReporter.Models;

namespace OtSnapshotReporter.Analysis;

public static class Analyzers
{
    public static IEnumerable<Finding> AnalyzeServices(IEnumerable<ServiceRecord> records, ExpectedServicesConfig config)
    {
        var actual = records.GroupBy(x => Helpers.Key(x.Server, x.Name), StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var expected in config.Services)
        {
            if (!actual.TryGetValue(Helpers.Key(expected.Server, expected.Name), out var service))
            {
                yield return Finding.Create("services", expected.Server, expected.Name, Severity.High, "Expected service is missing");
                continue;
            }

            if (!Helpers.EqualsText(service.Status, expected.ExpectedStatus))
            {
                var severity = Helpers.ParseSeverity(expected.SeverityIfStopped, Severity.Critical);
                yield return Finding.Create("services", service.Server, service.Name, severity, $"Service status is {service.Status}; expected {expected.ExpectedStatus}");
            }

            if (!string.IsNullOrWhiteSpace(expected.ExpectedStartupType) && !Helpers.EqualsText(service.StartupType, expected.ExpectedStartupType))
            {
                yield return Finding.Create("services", service.Server, service.Name, Severity.Medium, $"Startup type is {service.StartupType}; expected {expected.ExpectedStartupType}");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeDisks(IEnumerable<DiskRecord> records, Thresholds thresholds)
    {
        foreach (var disk in records)
        {
            if (disk.FreePercent < 0)
            {
                continue;
            }

            if (disk.FreePercent < thresholds.DiskFreePercentCritical)
            {
                yield return Finding.Create("disk", disk.Server, disk.Drive, Severity.Critical, $"Free space is {disk.FreePercent}%");
            }
            else if (disk.FreePercent < thresholds.DiskFreePercentWarning)
            {
                yield return Finding.Create("disk", disk.Server, disk.Drive, Severity.Medium, $"Free space is {disk.FreePercent}%");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeTasks(IEnumerable<TaskRecord> records, ExpectedTasksConfig config, Thresholds thresholds, DateTime? evaluationTime = null)
    {
        var taskRecords = records.ToList();
        var now = evaluationTime ?? DateTime.Now;
        foreach (var task in taskRecords)
        {
            if (task.LastTaskResult.HasValue && task.LastTaskResult.Value != 0)
            {
                yield return Finding.Create("scheduled_tasks", task.Server, task.TaskPath + task.TaskName, Severity.High, $"Last task result was {task.LastTaskResult}");
            }

            if (Helpers.TryParseTimestamp(task.LastRunTime, out var lastRun) && now.Subtract(lastRun).TotalHours > thresholds.TaskNotRunHoursWarning)
            {
                yield return Finding.Create("scheduled_tasks", task.Server, task.TaskPath + task.TaskName, Severity.Medium, $"Task has not run since {task.LastRunTime}");
            }
        }

        var actual = taskRecords.GroupBy(x => Helpers.Key(x.Server, x.TaskPath, x.TaskName), StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var expected in config.Tasks)
        {
            if (!actual.TryGetValue(Helpers.Key(expected.Server, expected.TaskPath, expected.TaskName), out var task))
            {
                yield return Finding.Create("scheduled_tasks", expected.Server, expected.TaskPath + expected.TaskName, Severity.High, "Expected task is missing");
                continue;
            }

            if (expected.ExpectedEnabled.HasValue && task.Enabled != expected.ExpectedEnabled.Value)
            {
                yield return Finding.Create("scheduled_tasks", task.Server, task.TaskPath + task.TaskName, Severity.High, $"Task enabled is {task.Enabled}; expected {expected.ExpectedEnabled}");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeUptime(IEnumerable<UptimeRecord> records, Thresholds thresholds, PreviousSnapshot previous)
    {
        if (!thresholds.RebootDetectionEnabled || previous.Uptimes.Count == 0)
        {
            yield break;
        }

        var previousByServer = previous.Uptimes.GroupBy(x => x.Server, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            if (previousByServer.TryGetValue(record.Server, out var old) &&
                Helpers.TryParseTimestamp(record.LastBootTime, out var currentBoot) &&
                Helpers.TryParseTimestamp(old.LastBootTime, out var oldBoot) &&
                currentBoot > oldBoot)
            {
                yield return Finding.Create("uptime", record.Server, "LastBootTime", Severity.High, $"Server rebooted since previous snapshot; current boot {record.LastBootTime}");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeSoftware(IEnumerable<SoftwareRecord> records, ExpectedSoftwareConfig config)
    {
        var softwareByKey = records.GroupBy(x => Helpers.Key(x.Server, x.Name), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var expected in config.Software)
        {
            if (!softwareByKey.TryGetValue(Helpers.Key(expected.Server, expected.Name), out var match))
            {
                yield return Finding.Create("software", expected.Server, expected.Name, Severity.High, "Expected software is missing");
            }
            else if (!Helpers.EqualsText(match.Version, expected.ExpectedVersion))
            {
                yield return Finding.Create("software", match.Server, match.Name, Severity.High, $"Version is {match.Version}; expected {expected.ExpectedVersion}");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeDrivers(IEnumerable<DriverRecord> records, ExpectedDriversConfig config)
    {
        var driversByKey = records.GroupBy(x => Helpers.Key(x.Server, x.Type, x.Name, x.Architecture), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        foreach (var expected in config.Drivers)
        {
            if (!driversByKey.TryGetValue(Helpers.Key(expected.Server, expected.Type, expected.Name, expected.Architecture), out var match))
            {
                yield return Finding.Create("drivers", expected.Server, expected.Name, Severity.High, "Expected driver/provider is missing");
            }
            else if (!Helpers.EqualsText(match.Version, expected.ExpectedVersion))
            {
                yield return Finding.Create("drivers", match.Server, match.Name, Severity.Medium, $"Version is {match.Version}; expected {expected.ExpectedVersion}");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeCollectionErrors(IEnumerable<ErrorRecord> records)
    {
        foreach (var record in records)
        {
            yield return Finding.Create("collection_errors", record.Server, "Collector", Severity.Critical, record.Error);
        }
    }

    public static IEnumerable<Finding> AnalyzeMissingServers(ServersConfig config, IEnumerable<string> observedServers)
    {
        var observed = observedServers
            .Where(server => !string.IsNullOrWhiteSpace(server))
            .Select(server => server.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var configured in config.Servers.Where(server => !string.IsNullOrWhiteSpace(server.Name)))
        {
            if (!observed.Contains(configured.Name))
            {
                yield return Finding.Create(
                    "collection_errors",
                    configured.Name,
                    "Snapshot",
                    Severity.Critical,
                    "No collector data was received for configured server");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeEventLogs(IEnumerable<EventLogSummaryRecord> records)
    {
        foreach (var record in records)
        {
            if (record.Level == 1)
            {
                yield return Finding.Create("event_logs", record.Server, $"{record.LogName}/{record.Source}", Severity.High, $"{record.Count} critical events in the last {record.WindowHours} hours; newest event ID {record.NewestEventId}");
            }
            else if (record.Level == 2)
            {
                yield return Finding.Create("event_logs", record.Server, $"{record.LogName}/{record.Source}", Severity.Medium, $"{record.Count} error events in the last {record.WindowHours} hours; newest event ID {record.NewestEventId}");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeFileShares(IEnumerable<FileShareRecord> records)
    {
        foreach (var share in records)
        {
            if (!share.Reachable)
            {
                var message = string.IsNullOrWhiteSpace(share.Error) ? "Share is unreachable" : $"Share is unreachable: {share.Error}";
                yield return Finding.Create("file_shares", share.Server, share.Name ?? share.Path, Severity.High, message);
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeBackups(IEnumerable<BackupFreshnessRecord> records)
    {
        foreach (var backup in records)
        {
            if (!backup.Exists)
            {
                yield return Finding.Create("backups", backup.Server, backup.Name ?? backup.Path, Severity.High, "Expected backup/export path is missing");
                continue;
            }

            if (string.IsNullOrWhiteSpace(backup.NewestFile))
            {
                yield return Finding.Create("backups", backup.Server, backup.Name ?? backup.Path, Severity.High, "Backup/export folder is empty");
                continue;
            }

            if (backup.AgeHours.HasValue && backup.MaxAgeHours.HasValue && backup.AgeHours.Value > backup.MaxAgeHours.Value)
            {
                yield return Finding.Create("backups", backup.Server, backup.Name ?? backup.Path, Severity.High, $"Newest file is {backup.AgeHours:0.##} hours old; threshold is {backup.MaxAgeHours:0.##} hours");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeOdbcDsns(IEnumerable<OdbcDsnRecord> records)
    {
        foreach (var dsn in records)
        {
            if (!dsn.ConnectionPassed)
            {
                yield return Finding.Create("odbc_dsns", dsn.Server, dsn.DsnName, Severity.High, $"DSN connection test failed (driver: {dsn.DriverName})");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeCertificates(IEnumerable<CertificateRecord> records)
    {
        foreach (var cert in records)
        {
            if (cert.DaysUntilExpiry < 0)
            {
                yield return Finding.Create("certificates", cert.Server, cert.Subject, Severity.Critical, $"Certificate expired {Math.Abs((long)cert.DaysUntilExpiry)} days ago");
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

    public static IEnumerable<Finding> AnalyzeSqlAgentJobs(IEnumerable<SqlAgentJobRecord> records, DateTime? evaluationTime = null)
    {
        var now = evaluationTime ?? DateTime.UtcNow;
        foreach (var job in records)
        {
            if (!job.Enabled)
            {
                continue;
            }

            var subject = $"{job.Instance}\\{job.JobName}";
            if (job.LastRunStatus == 0)
            {
                yield return Finding.Create("sql_agent_jobs", job.Server, subject, Severity.High, $"SQL Agent job failed: {job.LastRunMessage ?? "last run status 0"}");
            }
            else if (job.LastRunStatus == 2)
            {
                yield return Finding.Create("sql_agent_jobs", job.Server, subject, Severity.Medium, "SQL Agent job is retrying");
            }
            else if (job.LastRunStatus == 3)
            {
                yield return Finding.Create("sql_agent_jobs", job.Server, subject, Severity.Medium, "SQL Agent job was cancelled");
            }

            if (TryParseSqlAgentDate(job.LastRunDate, out var lastRun) && now.Subtract(lastRun).TotalHours > 48)
            {
                yield return Finding.Create("sql_agent_jobs", job.Server, subject, Severity.Medium, "SQL Agent job has not run in 48 hours");
            }
        }
    }

    public static IEnumerable<Finding> AnalyzeSsrsSubscriptions(IEnumerable<SsrsSubscriptionRecord> records)
    {
        foreach (var subscription in records)
        {
            var subject = $"{subscription.Instance}\\{subscription.ReportPath}";
            if (!subscription.Enabled)
            {
                continue;
            }

            if (!subscription.OwnerExists)
            {
                yield return Finding.Create("ssrs_subscriptions", subscription.Server, subject, Severity.High, $"SSRS subscription owner is missing or disabled: {subscription.Owner ?? "(unknown)"}");
            }

            if (IsFailedSsrsStatus(subscription.LastStatus))
            {
                yield return Finding.Create("ssrs_subscriptions", subscription.Server, subject, Severity.High, $"SSRS subscription last status: {subscription.LastStatus}");
            }
        }
    }

    private static bool TryParseSqlAgentDate(string? value, out DateTime result)
    {
        result = default;
        if (!int.TryParse(value, out var dateInt) || dateInt <= 0)
        {
            return false;
        }

        var year = dateInt / 10000;
        var month = dateInt % 10000 / 100;
        var day = dateInt % 100;
        if (year < 2000 || month is < 1 or > 12 || day is < 1 or > 31)
        {
            return false;
        }

        try
        {
            result = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
    }

    private static bool IsFailedSsrsStatus(string? status)
    {
        if (string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        if (status.Contains("0 errors", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("succeeded", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return status.Contains("fail", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("error", StringComparison.OrdinalIgnoreCase);
    }
}
