using System.Globalization;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Cannot read {path}: {ex.Message}");
            return default;
        }
    }

    public static List<T> LoadRecords<T>(string path, JsonSerializerOptions options, Func<T, bool> isValid, string recordType) where T : class
    {
        if (!File.Exists(path))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                Console.Error.WriteLine($"Warning: Expected an array of {recordType} records in {path}.");
                return [];
            }

            var valid = new List<T>();
            var ignored = 0;
            foreach (var element in document.RootElement.EnumerateArray())
            {
                try
                {
                    var record = element.Deserialize<T>(options);
                    if (record is not null && isValid(record))
                    {
                        valid.Add(record);
                    }
                    else
                    {
                        ignored++;
                    }
                }
                catch (JsonException)
                {
                    ignored++;
                }
            }

            if (ignored > 0)
            {
                Console.Error.WriteLine($"Warning: Ignored {ignored} invalid {recordType} record(s) in {path}.");
            }

            return valid;
        }
        catch (JsonException ex)
        {
            Console.Error.WriteLine($"Warning: Failed to parse {path}: {ex.Message}");
            return [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Cannot read {path}: {ex.Message}");
            return [];
        }
    }

    public static string ResolveRawRoot(string inputPath, string outputPath)
    {
        var single = Path.Combine(inputPath, "raw");
        if (Directory.Exists(single))
        {
            return single;
        }

        if (Directory.Exists(inputPath) && GetFilesSafely(inputPath, "*.json", "JSON input files").Length > 0)
        {
            return inputPath;
        }

        if (!Directory.Exists(inputPath))
        {
            Console.Error.WriteLine($"Error: Input path does not exist: {inputPath}");
            return Path.Combine(inputPath, "raw");
        }

        var serverRawFolders = FindPerServerRawFolders(inputPath);

        if (serverRawFolders.Count == 0)
        {
            Console.Error.WriteLine($"Warning: No raw/ or server directories found under {inputPath}");
            return Path.Combine(inputPath, "raw");
        }

        var mergedDir = CreateMergeRoot(outputPath);
        var groupedFiles = serverRawFolders
            .SelectMany(raw => GetFilesSafely(raw, "*.json", "raw input files"))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
        var mergedErrorItems = new List<JsonElement>();

        foreach (var group in groupedFiles)
        {
            var mergedItems = new List<JsonElement>();
            foreach (var file in group)
            {
                var server = GetServerName(Path.GetDirectoryName(file));
                try
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
                catch (JsonException ex)
                {
                    Console.Error.WriteLine($"Warning: Failed to merge {file}: {ex.Message}");
                    mergedErrorItems.Add(JsonSerializer.SerializeToElement(new
                    {
                        server,
                        error = $"Failed to parse {Path.GetFileName(file)}: {ex.Message}",
                        run = DateTime.Now.ToString("s")
                    }));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Console.Error.WriteLine($"Warning: Cannot merge {file}: {ex.Message}");
                    mergedErrorItems.Add(JsonSerializer.SerializeToElement(new
                    {
                        server,
                        error = $"Cannot read {Path.GetFileName(file)}: {ex.Message}",
                        run = DateTime.Now.ToString("s")
                    }));
                }
            }

            if (string.Equals(group.Key, "_errors.json", StringComparison.OrdinalIgnoreCase))
            {
                mergedErrorItems.AddRange(mergedItems);
            }
            else
            {
                Writing.WriteTextAtomically(Path.Combine(mergedDir, group.Key ?? "unknown.json"), JsonSerializer.Serialize(mergedItems, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        if (mergedErrorItems.Count > 0)
        {
            Writing.WriteTextAtomically(Path.Combine(mergedDir, "_errors.json"), JsonSerializer.Serialize(mergedErrorItems, new JsonSerializerOptions { WriteIndented = true }));
        }

        return mergedDir;
    }

    public static void CopyRawInputs(string source, string destination)
    {
        if (!Directory.Exists(source))
        {
            return;
        }

        foreach (var file in GetFilesSafely(source, "*.json", "raw input files"))
        {
            try
            {
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Warning: Cannot copy {file}: {ex.Message}");
            }
        }
    }

    public static PreviousSnapshot LoadPreviousSnapshot(string? path, JsonSerializerOptions json)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return PreviousSnapshot.Empty;
        }

        var raw = Path.Combine(path, "raw");
        string? temporaryMergeRoot = null;
        if (!Directory.Exists(raw) && Directory.Exists(path) && GetFilesSafely(path, "*.json", "previous snapshot files").Length > 0)
        {
            raw = path;
        }
        else if (!Directory.Exists(raw) && HasPerServerRawFolders(path))
        {
            temporaryMergeRoot = Path.Combine(Path.GetTempPath(), $"ot-snapshot-previous-{Guid.NewGuid():N}");
            raw = ResolveRawRoot(path, temporaryMergeRoot);
        }

        try
        {
            return new PreviousSnapshot(
                LoadRecords<ServiceRecord>(Path.Combine(raw, "services.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Name), "previous service"),
                LoadRecords<DiskRecord>(Path.Combine(raw, "disk_space.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Drive), "previous disk"),
                LoadRecords<TaskRecord>(Path.Combine(raw, "scheduled_tasks.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.TaskName), "previous scheduled task"),
                LoadRecords<UptimeRecord>(Path.Combine(raw, "uptime.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.LastBootTime), "previous uptime"),
                LoadRecords<SoftwareRecord>(Path.Combine(raw, "software.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Name), "previous software"),
                LoadRecords<DriverRecord>(Path.Combine(raw, "odbc_oledb.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Type) && !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Architecture), "previous driver"),
                LoadRecords<EventLogSummaryRecord>(Path.Combine(raw, "event_log_summary.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.LogName) && !string.IsNullOrWhiteSpace(x.Source), "previous event log"),
                LoadRecords<FileShareRecord>(Path.Combine(raw, "file_shares.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Path), "previous file-share"),
                LoadRecords<BackupFreshnessRecord>(Path.Combine(raw, "backup_freshness.json"), json, x => !string.IsNullOrWhiteSpace(x.Server) && !string.IsNullOrWhiteSpace(x.Path), "previous backup"));
        }
        finally
        {
            if (temporaryMergeRoot is not null && Directory.Exists(temporaryMergeRoot))
            {
                TryDeleteTemporaryMergeRoot(temporaryMergeRoot);
            }
        }
    }

    private static bool HasPerServerRawFolders(string path) =>
        FindPerServerRawFolders(path).Count > 0;

    // Per-server runs nest raw data under a timestamp; one current folder is selected per server.
    private static List<string> FindPerServerRawFolders(string inputPath)
    {
        if (!Directory.Exists(inputPath))
        {
            return [];
        }

        return GetDirectoriesSafely(inputPath, "per-server folders")
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .Select(FindLatestRawFolder)
            .Where(path => path is not null)
            .Select(path => path!)
            .ToList();
    }

    private static string? FindLatestRawFolder(string serverPath)
    {
        if (IsReportOrCollectionFolder(serverPath))
        {
            return null;
        }

        var direct = Path.Combine(serverPath, "raw");
        if (Directory.Exists(direct))
        {
            return direct;
        }

        var timestampFolders = GetDirectoriesSafely(serverPath, "timestamp folders");

        return timestampFolders
            .Select(folder => new
            {
                Folder = folder,
                Raw = Path.Combine(folder, "raw"),
                Timestamp = TryParseSnapshotFolder(Path.Combine(folder, "raw"), out var timestamp) ? timestamp : (DateTime?)null
            })
            .Where(candidate => candidate.Timestamp.HasValue &&
                Directory.Exists(candidate.Raw) &&
                !File.Exists(Path.Combine(candidate.Folder, "index.html")))
            .OrderByDescending(candidate => candidate.Timestamp.HasValue)
            .ThenByDescending(candidate => candidate.Timestamp ?? DateTime.MinValue)
            .ThenByDescending(candidate => GetLastWriteTimeUtcOrMin(candidate.Raw))
            .Select(candidate => candidate.Raw)
            .FirstOrDefault();
    }

    private static bool IsReportOrCollectionFolder(string path)
    {
        var folderName = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(folderName))
        {
            return false;
        }

        var isReport = File.Exists(Path.Combine(path, "index.html")) &&
            DateTime.TryParseExact(
                folderName,
                ["yyyy-MM-dd_HHmmss", "yyyy-MM-dd_HHmm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);
        if (isReport)
        {
            return true;
        }

        const string collectionPrefix = "collection_";
        return folderName.StartsWith(collectionPrefix, StringComparison.OrdinalIgnoreCase) &&
            DateTime.TryParseExact(
                folderName[collectionPrefix.Length..],
                "yyyy-MM-dd_HHmmss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out _);
    }

    private static bool TryParseSnapshotFolder(string rawPath, out DateTime timestamp)
    {
        var folderName = Directory.GetParent(rawPath)?.Name;
        return DateTime.TryParseExact(
            folderName,
            ["yyyy-MM-dd_HHmmss", "yyyy-MM-dd_HHmm"],
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out timestamp);
    }

    private static string GetServerName(string? rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return "unknown";
        }

        var serverFolder = Directory.GetParent(rawPath);
        if (serverFolder is not null && TryParseSnapshotFolder(rawPath, out _))
        {
            serverFolder = serverFolder.Parent;
        }

        return string.IsNullOrWhiteSpace(serverFolder?.Name) ? "unknown" : serverFolder.Name;
    }

    private static string CreateMergeRoot(string outputPath)
    {
        var preferred = Path.Combine(outputPath, "merged_raw");
        if (Directory.Exists(preferred))
        {
            try
            {
                Directory.Delete(preferred, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Warning: Could not reset merged raw staging: {ex.Message}");
                return CreateFallbackMergeRoot(outputPath);
            }
        }
        else if (File.Exists(preferred))
        {
            Console.Error.WriteLine($"Warning: Merged raw staging path is a file: {preferred}");
            return CreateFallbackMergeRoot(outputPath);
        }

        try
        {
            Directory.CreateDirectory(preferred);
            return preferred;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Could not create merged raw staging: {ex.Message}");
            return CreateFallbackMergeRoot(outputPath);
        }
    }

    private static string CreateFallbackMergeRoot(string outputPath)
    {
        var fallback = Path.Combine(outputPath, $"merged_raw_{Guid.NewGuid():N}");
        Directory.CreateDirectory(fallback);
        return fallback;
    }

    private static void TryDeleteTemporaryMergeRoot(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Could not remove temporary previous snapshot merge: {ex.Message}");
        }
    }

    private static DateTime GetLastWriteTimeUtcOrMin(string path)
    {
        try
        {
            return Directory.GetLastWriteTimeUtc(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MinValue;
        }
    }

    private static string[] GetFilesSafely(string path, string searchPattern, string description)
    {
        try
        {
            return Directory.GetFiles(path, searchPattern);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Cannot enumerate {description} in {path}: {ex.Message}");
            return [];
        }
    }

    private static string[] GetDirectoriesSafely(string path, string description)
    {
        try
        {
            return Directory.GetDirectories(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: Cannot enumerate {description} in {path}: {ex.Message}");
            return [];
        }
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
