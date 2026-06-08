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
}
