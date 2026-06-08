using System.Text.Json;
using OtSnapshotReporter.Models;

namespace OtSnapshotReporter.Infrastructure;

public static class Loading
{
    public static T? LoadJson<T>(string path, JsonSerializerOptions options) where T : class
    {
        if (!File.Exists(path))
        {
            return default;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path), options);
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Warning: Failed to parse {path}: {ex.Message}");
            return default;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Warning: Cannot read {path}: {ex.Message}");
            return default;
        }
    }

    public static string ResolveRawRoot(string inputPath, string outputPath)
    {
        var single = Path.Combine(inputPath, "raw");
        if (Directory.Exists(single))
        {
            return single;
        }

        if (Directory.Exists(inputPath) && Directory.GetFiles(inputPath, "*.json").Length > 0)
        {
            return inputPath;
        }

        if (!Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: Input path does not exist: {inputPath}");
            return Path.Combine(inputPath, "raw");
        }

        var serverDirs = Directory.GetDirectories(inputPath)
            .Select(d => new { Path = d, Raw = Path.Combine(d, "raw") })
            .Where(x => Directory.Exists(x.Raw))
            .ToList();

        if (serverDirs.Count == 0)
        {
            Console.Error.WriteLine($"Warning: No raw/ or server directories found under {inputPath}");
            return Path.Combine(inputPath, "raw");
        }

        var mergedDir = Path.Combine(outputPath, "merged_raw");
        if (Directory.Exists(mergedDir))
        {
            Directory.Delete(mergedDir, recursive: true);
        }

        Directory.CreateDirectory(mergedDir);
        var groupedFiles = serverDirs
            .SelectMany(sd => Directory.GetFiles(sd.Raw, "*.json"))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

        foreach (var group in groupedFiles)
        {
            var mergedItems = new List<JsonElement>();
            foreach (var file in group)
            {
                using var document = JsonDocument.Parse(File.ReadAllText(file));
                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    mergedItems.AddRange(document.RootElement.EnumerateArray().Select(x => x.Clone()));
                }
                else
                {
                    mergedItems.Add(document.RootElement.Clone());
                }
            }

            File.WriteAllText(Path.Combine(mergedDir, group.Key ?? "unknown.json"), JsonSerializer.Serialize(mergedItems, new JsonSerializerOptions { WriteIndented = true }));
        }

        return mergedDir;
    }

    public static void CopyRawInputs(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        foreach (var file in Directory.GetFiles(source, "*.json"))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    public static PreviousSnapshot LoadPreviousSnapshot(string? path, JsonSerializerOptions json)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PreviousSnapshot.Empty;
        }

        var raw = Path.Combine(path, "raw");
        return new PreviousSnapshot(
            LoadJson<List<ServiceRecord>>(Path.Combine(raw, "services.json"), json) ?? [],
            LoadJson<List<DiskRecord>>(Path.Combine(raw, "disk_space.json"), json) ?? [],
            LoadJson<List<TaskRecord>>(Path.Combine(raw, "scheduled_tasks.json"), json) ?? [],
            LoadJson<List<UptimeRecord>>(Path.Combine(raw, "uptime.json"), json) ?? [],
            LoadJson<List<SoftwareRecord>>(Path.Combine(raw, "software.json"), json) ?? [],
            LoadJson<List<DriverRecord>>(Path.Combine(raw, "odbc_oledb.json"), json) ?? [],
            LoadJson<List<EventLogSummaryRecord>>(Path.Combine(raw, "event_log_summary.json"), json) ?? [],
            LoadJson<List<FileShareRecord>>(Path.Combine(raw, "file_shares.json"), json) ?? [],
            LoadJson<List<BackupFreshnessRecord>>(Path.Combine(raw, "backup_freshness.json"), json) ?? []);
    }

    public static List<ModuleDescriptor> GetModuleDescriptors() =>
    [
        new("software", "Software", "software_matrix.html", "software.json", "expected_software.json", true),
        new("drivers", "ODBC/OLE DB", "odbc_oledb_inventory.html", "odbc_oledb.json", "expected_drivers.json", true),
        new("services", "Services", "services.html", "services.json", "expected_services.json", false),
        new("scheduled_tasks", "Scheduled Tasks", "scheduled_tasks.html", "scheduled_tasks.json", "expected_tasks.json", false),
        new("uptime", "Reboots", "reboots.html", "uptime.json", null, false),
        new("disk", "Disk Space", "disk_space.html", "disk_space.json", null, false),
        new("event_logs", "Event Logs", "event_log_summary.html", "event_log_summary.json", "event_log_config.json", false),
        new("file_shares", "File Shares", "file_shares.html", "file_shares.json", "shares.json", false),
        new("backups", "Backups", "backup_freshness.html", "backup_freshness.json", "expected_paths.json", false),
        new("odbc_dsns", "ODBC DSNs", "odbc_dsn_tests.html", "odbc_dsn_tests.json", null, false),
        new("certificates", "Certificates", "certificates.html", "certificates.json", null, false),
        new("sql_agent_jobs", "SQL Agent Jobs", "sql_agent_jobs.html", "sql_agent_jobs.json", null, false),
        new("ssrs_subscriptions", "SSRS Subscriptions", "ssrs_subscriptions.html", "ssrs_subscriptions.json", null, false),
        new("correlation", "Correlation", "correlation.html", "", null, false),
        new("collection_errors", "Collection Errors", "errors.html", "_errors.json", null, false)
    ];
}
