using System.Globalization;
using System.Text;
using System.Text.Json;
using OtSnapshotReporter.Models;

namespace OtSnapshotReporter.Infrastructure;

public static class Writing
{
    public static void WriteSummaryJson(string path, string outputPath, IReadOnlyCollection<Finding> findings)
    {
        var summary = new
        {
            timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            outputPath,
            counts = findings.GroupBy(x => x.Severity).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            total = findings.Count
        };
        File.WriteAllText(path, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public static string GetAvailableReportRoot(string outputPath, DateTime timestamp)
    {
        var candidateTime = timestamp;
        while (true)
        {
            var candidate = Path.Combine(outputPath, candidateTime.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
            if (!Directory.Exists(candidate) && !File.Exists(candidate))
            {
                return candidate;
            }

            candidateTime = candidateTime.AddSeconds(1);
        }
    }

    public static void CleanupOldSnapshots(string outputPath, int retentionDays)
    {
        if (!Directory.Exists(outputPath) || retentionDays <= 0)
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-1 * retentionDays);
        foreach (var dir in Directory.GetDirectories(outputPath))
        {
            var name = Path.GetFileName(dir);
            if (name.Length >= 15 &&
                DateTime.TryParseExact(name[..15], "yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) &&
                timestamp < cutoff)
            {
                Directory.Delete(dir, recursive: true);
                Console.WriteLine($"Cleaned up old snapshot: {name}");
            }
        }
    }

    public static void CleanupOldCollectionStaging(string outputPath, int retentionDays)
    {
        if (!Directory.Exists(outputPath) || retentionDays <= 0)
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-1 * retentionDays);
        foreach (var dir in Directory.GetDirectories(outputPath))
        {
            var name = Path.GetFileName(dir);
            const string prefix = "collection_";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !DateTime.TryParseExact(name[prefix.Length..], "yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) ||
                timestamp >= cutoff)
            {
                continue;
            }

            Directory.Delete(dir, recursive: true);
            Console.WriteLine($"Cleaned up old collection staging: {name}");
        }
    }

    public static void WriteBaselineConfigs(string configPath, IReadOnlyCollection<ServiceRecord> services, IReadOnlyCollection<TaskRecord> tasks, IReadOnlyCollection<SoftwareRecord> software, IReadOnlyCollection<DriverRecord> drivers)
    {
        Directory.CreateDirectory(configPath);
        var json = new JsonSerializerOptions { WriteIndented = true };

        var servicesConfig = new ExpectedServicesConfig(services.OrderBy(x => x.Server).ThenBy(x => x.Name).Select(x => new ExpectedService(x.Server, x.Name, x.Status, x.StartupType, "Critical")).ToList());
        File.WriteAllText(Path.Combine(configPath, "expected_services.json"), JsonSerializer.Serialize(servicesConfig, json));

        var tasksConfig = new ExpectedTasksConfig(tasks.OrderBy(x => x.Server).ThenBy(x => x.TaskPath).ThenBy(x => x.TaskName).Select(x => new ExpectedTask(x.Server, x.TaskPath, x.TaskName, x.Enabled)).ToList());
        File.WriteAllText(Path.Combine(configPath, "expected_tasks.json"), JsonSerializer.Serialize(tasksConfig, json));

        var softwareConfig = new ExpectedSoftwareConfig(software.Where(x => !string.IsNullOrWhiteSpace(x.Version)).OrderBy(x => x.Server).ThenBy(x => x.Name).Select(x => new ExpectedSoftware(x.Server, x.Name, x.Version ?? "")).ToList());
        File.WriteAllText(Path.Combine(configPath, "expected_software.json"), JsonSerializer.Serialize(softwareConfig, json));

        var driversConfig = new ExpectedDriversConfig(drivers.Where(x => !string.IsNullOrWhiteSpace(x.Version)).OrderBy(x => x.Server).ThenBy(x => x.Type).ThenBy(x => x.Name).ThenBy(x => x.Architecture).Select(x => new ExpectedDriver(x.Server, x.Type, x.Name, x.Architecture, x.Version ?? "")).ToList());
        File.WriteAllText(Path.Combine(configPath, "expected_drivers.json"), JsonSerializer.Serialize(driversConfig, json));
    }
}
