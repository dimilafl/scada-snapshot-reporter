using OtSnapshotReporter.Infrastructure;
using OtSnapshotReporter.Models;

namespace OtSnapshotReporter.Diff;

public static class DiffEngine
{
    public sealed record FieldComparator<T>(Func<T, T, Finding?> Compare);

    public static IEnumerable<Finding> Diff<T>(
        IReadOnlyCollection<T> current,
        IReadOnlyCollection<T> previous,
        Func<T, string> keySelector,
        Func<T, Finding> missingFinding,
        Func<T, Finding?> newFinding,
        IReadOnlyCollection<FieldComparator<T>> fieldComparators)
    {
        if (previous.Count == 0)
        {
            yield break;
        }

        var currentByKey = current.GroupBy(keySelector).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);
        var previousByKey = previous.GroupBy(keySelector).ToDictionary(x => x.Key, x => x.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var (key, old) in previousByKey)
        {
            if (!currentByKey.TryGetValue(key, out var currentItem))
            {
                yield return missingFinding(old);
                continue;
            }

            foreach (var comparator in fieldComparators)
            {
                var finding = comparator.Compare(currentItem, old);
                if (finding is not null)
                {
                    yield return finding;
                }
            }
        }

        foreach (var (key, currentItem) in currentByKey)
        {
            if (!previousByKey.ContainsKey(key))
            {
                var finding = newFinding(currentItem);
                if (finding is not null)
                {
                    yield return finding;
                }
            }
        }
    }

    public static IEnumerable<Finding> DiffServices(IReadOnlyCollection<ServiceRecord> records, IReadOnlyCollection<ServiceRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, x.Name),
            old => Finding.Create("services", old.Server, old.Name, Severity.Medium, "Service disappeared since previous snapshot"),
            current => Finding.Create("services", current.Server, current.Name, Severity.Low, "New service detected since previous snapshot"),
            [
                new((current, old) => !Helpers.EqualsText(current.Status, old.Status) ? Finding.Create("services", current.Server, current.Name, Severity.Medium, $"Service status changed from {old.Status} to {current.Status}") : null),
                new((current, old) => !Helpers.EqualsText(current.StartupType, old.StartupType) ? Finding.Create("services", current.Server, current.Name, Severity.Medium, $"Startup type changed from {old.StartupType} to {current.StartupType}") : null),
                new((current, old) => !Helpers.EqualsText(current.StartName, old.StartName) ? Finding.Create("services", current.Server, current.Name, Severity.Low, $"Run-as account changed from {old.StartName} to {current.StartName}") : null)
            ]);

    public static IEnumerable<Finding> DiffDisks(IReadOnlyCollection<DiskRecord> records, IReadOnlyCollection<DiskRecord> previous, Thresholds thresholds) =>
        Diff(records, previous, x => Helpers.Key(x.Server, x.Drive),
            old => Finding.Create("disk", old.Server, old.Drive, Severity.Medium, "Disk disappeared since previous snapshot"),
            _ => null,
            [
                new((current, old) =>
                {
                    var drop = old.FreePercent - current.FreePercent;
                    return drop >= thresholds.DiskDropPercentWarning
                        ? Finding.Create("disk", current.Server, current.Drive, Severity.Low, $"Free space dropped by {drop:0.##} percentage points since previous snapshot")
                        : null;
                })
            ]);

    public static IEnumerable<Finding> DiffTasks(IReadOnlyCollection<TaskRecord> records, IReadOnlyCollection<TaskRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, x.TaskPath + x.TaskName),
            old => Finding.Create("scheduled_tasks", old.Server, old.TaskPath + old.TaskName, Severity.Medium, "Task disappeared since previous snapshot"),
            current => Finding.Create("scheduled_tasks", current.Server, current.TaskPath + current.TaskName, Severity.Low, "New task detected since previous snapshot"),
            [
                new((current, old) => current.Enabled != old.Enabled ? Finding.Create("scheduled_tasks", current.Server, current.TaskPath + current.TaskName, Severity.High, $"Enabled changed from {old.Enabled} to {current.Enabled}") : null),
                new((current, old) => !Helpers.EqualsText(current.RunAs, old.RunAs) ? Finding.Create("scheduled_tasks", current.Server, current.TaskPath + current.TaskName, Severity.Medium, $"Run-as account changed from {old.RunAs} to {current.RunAs}") : null),
                new((current, old) => !Helpers.EqualsText(current.Action, old.Action) ? Finding.Create("scheduled_tasks", current.Server, current.TaskPath + current.TaskName, Severity.High, "Task action changed since previous snapshot") : null)
            ]);

    public static IEnumerable<Finding> DiffSoftware(IReadOnlyCollection<SoftwareRecord> records, IReadOnlyCollection<SoftwareRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, x.Name),
            old => Finding.Create("software", old.Server, old.Name, Severity.Low, "Software disappeared since previous snapshot"),
            current => Finding.Create("software", current.Server, current.Name, Severity.Low, "New software detected since previous snapshot"),
            [new((current, old) => !Helpers.EqualsText(current.Version, old.Version) ? Finding.Create("software", current.Server, current.Name, Severity.Medium, $"Version changed from {old.Version} to {current.Version}") : null)]);

    public static IEnumerable<Finding> DiffDrivers(IReadOnlyCollection<DriverRecord> records, IReadOnlyCollection<DriverRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, $"{x.Type}|{x.Name}|{x.Architecture}"),
            old => Finding.Create("drivers", old.Server, old.Name, Severity.Medium, "Driver/provider disappeared since previous snapshot"),
            current => Finding.Create("drivers", current.Server, current.Name, Severity.Low, "New driver/provider detected since previous snapshot"),
            [new((current, old) => !Helpers.EqualsText(current.Version, old.Version) ? Finding.Create("drivers", current.Server, current.Name, Severity.Medium, $"Version changed from {old.Version} to {current.Version}") : null)]);

    public static IEnumerable<Finding> DiffEventLogs(IReadOnlyCollection<EventLogSummaryRecord> records, IReadOnlyCollection<EventLogSummaryRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, $"{x.LogName}|{x.Source}"),
            old => Finding.Create("event_logs", old.Server, $"{old.LogName}/{old.Source}", Severity.Low, "Event source no longer present"),
            current => current.Count > 5 ? Finding.Create("event_logs", current.Server, $"{current.LogName}/{current.Source}", Severity.Low, $"New event source with {current.Count} events") : null,
            [new((current, old) => current.Count > old.Count * 2 && current.Count > 10 ? Finding.Create("event_logs", current.Server, $"{current.LogName}/{current.Source}", Severity.Medium, $"Event count rose from {old.Count} to {current.Count}") : null)]);

    public static IEnumerable<Finding> DiffFileShares(IReadOnlyCollection<FileShareRecord> records, IReadOnlyCollection<FileShareRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, x.Path),
            _ => null!,
            _ => null,
            [
                new((current, old) => old.Reachable && !current.Reachable ? Finding.Create("file_shares", current.Server, current.Name ?? current.Path, Severity.High, "Share was reachable previously, now unreachable") : null),
                new((current, old) => !old.Reachable && current.Reachable ? Finding.Create("file_shares", current.Server, current.Name ?? current.Path, Severity.Medium, "Share recovered since previous snapshot") : null)
            ]);

    public static IEnumerable<Finding> DiffBackups(IReadOnlyCollection<BackupFreshnessRecord> records, IReadOnlyCollection<BackupFreshnessRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, x.Path),
            _ => null!,
            _ => null,
            [
                new((current, old) => old.Exists && !current.Exists ? Finding.Create("backups", current.Server, current.Name ?? current.Path, Severity.High, "Backup/export path disappeared since previous snapshot") : null),
                new((current, old) => current.AgeHours.HasValue && old.AgeHours.HasValue && current.AgeHours.Value > old.AgeHours.Value * 3 ? Finding.Create("backups", current.Server, current.Name ?? current.Path, Severity.Medium, $"Newest file age increased from {old.AgeHours:0.#}h to {current.AgeHours:0.#}h") : null)
            ]);

    public static IEnumerable<Finding> DiffOdbcDsns(IReadOnlyCollection<OdbcDsnRecord> records, IReadOnlyCollection<OdbcDsnRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, x.DsnName, x.Type, x.Architecture),
            old => Finding.Create("odbc_dsns", old.Server, old.DsnName, Severity.Medium, "ODBC DSN disappeared since previous snapshot"),
            _ => null,
            [
                new((current, old) => current.ConnectionPassed == old.ConnectionPassed
                    ? null
                    : current.ConnectionPassed
                        ? Finding.Create("odbc_dsns", current.Server, current.DsnName, Severity.Medium, "ODBC DSN connection recovered since previous snapshot")
                        : Finding.Create("odbc_dsns", current.Server, current.DsnName, Severity.High, "ODBC DSN connection changed from passed to failed")),
                new((current, old) => !Helpers.EqualsText(current.DriverName, old.DriverName)
                    ? Finding.Create("odbc_dsns", current.Server, current.DsnName, Severity.Medium, $"ODBC DSN driver changed from {old.DriverName} to {current.DriverName}")
                    : null)
            ]);

    public static IEnumerable<Finding> DiffCertificates(IReadOnlyCollection<CertificateRecord> records, IReadOnlyCollection<CertificateRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, x.Thumbprint, x.Subject, x.Store),
            old => Finding.Create("certificates", old.Server, old.Subject, Severity.Medium, "Certificate disappeared since previous snapshot"),
            _ => null,
            [
                new((current, old) => !Helpers.EqualsText(current.NotAfter, old.NotAfter)
                    ? Finding.Create("certificates", current.Server, current.Subject, Severity.Medium, $"Certificate expiration changed from {old.NotAfter} to {current.NotAfter}")
                    : null)
            ]);

    public static IEnumerable<Finding> DiffSqlAgentJobs(IReadOnlyCollection<SqlAgentJobRecord> records, IReadOnlyCollection<SqlAgentJobRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, x.Instance, x.JobName),
            old => Finding.Create("sql_agent_jobs", old.Server, $"{old.Instance}\\{old.JobName}", Severity.Medium, "SQL Agent job disappeared since previous snapshot"),
            _ => null,
            [
                new((current, old) => current.Enabled != old.Enabled
                    ? Finding.Create("sql_agent_jobs", current.Server, $"{current.Instance}\\{current.JobName}", Severity.High, $"SQL Agent job enabled state changed from {old.Enabled} to {current.Enabled}")
                    : null),
                new((current, old) => current.LastRunStatus != old.LastRunStatus
                    ? Finding.Create("sql_agent_jobs", current.Server, $"{current.Instance}\\{current.JobName}", current.LastRunStatus == 0 ? Severity.High : Severity.Medium, $"SQL Agent job last run status changed from {old.LastRunStatus?.ToString() ?? "unknown"} to {current.LastRunStatus?.ToString() ?? "unknown"}")
                    : null),
                new((current, old) => !Helpers.EqualsText(current.JobOwner, old.JobOwner)
                    ? Finding.Create("sql_agent_jobs", current.Server, $"{current.Instance}\\{current.JobName}", Severity.Low, $"SQL Agent job owner changed from {old.JobOwner} to {current.JobOwner}")
                    : null)
            ]);

    public static IEnumerable<Finding> DiffSsrsSubscriptions(IReadOnlyCollection<SsrsSubscriptionRecord> records, IReadOnlyCollection<SsrsSubscriptionRecord> previous) =>
        Diff(records, previous, x => Helpers.Key(x.Server, x.Instance, x.ReportPath, x.SubscriptionDescription),
            old => Finding.Create("ssrs_subscriptions", old.Server, $"{old.Instance}\\{old.ReportPath}", Severity.Medium, "SSRS subscription disappeared since previous snapshot"),
            _ => null,
            [
                new((current, old) => current.Enabled != old.Enabled
                    ? Finding.Create("ssrs_subscriptions", current.Server, $"{current.Instance}\\{current.ReportPath}", Severity.High, $"SSRS subscription enabled state changed from {old.Enabled} to {current.Enabled}")
                    : null),
                new((current, old) => current.OwnerExists != old.OwnerExists
                    ? Finding.Create("ssrs_subscriptions", current.Server, $"{current.Instance}\\{current.ReportPath}", current.OwnerExists ? Severity.Medium : Severity.High, $"SSRS subscription owner availability changed from {old.OwnerExists} to {current.OwnerExists}")
                    : null),
                new((current, old) => !Helpers.EqualsText(current.LastStatus, old.LastStatus)
                    ? Finding.Create("ssrs_subscriptions", current.Server, $"{current.Instance}\\{current.ReportPath}", Severity.Medium, $"SSRS subscription status changed from {old.LastStatus} to {current.LastStatus}")
                    : null),
                new((current, old) => current.OwnerExists && old.OwnerExists && !Helpers.EqualsText(current.Owner, old.Owner)
                    ? Finding.Create("ssrs_subscriptions", current.Server, $"{current.Instance}\\{current.ReportPath}", Severity.Low, $"SSRS subscription owner changed from {old.Owner} to {current.Owner}")
                    : null)
            ]);
}
