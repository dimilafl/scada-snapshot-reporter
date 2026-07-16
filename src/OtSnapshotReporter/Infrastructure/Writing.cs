using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OtSnapshotReporter.Models;

namespace OtSnapshotReporter.Infrastructure;

public static class Writing
{
    public static void WriteTextAtomically(string path, string contents, Encoding? encoding = null)
    {
        var fullPath = Path.GetFullPath(path);
        var directory = Path.GetDirectoryName(fullPath) ?? Directory.GetCurrentDirectory();
        var leaf = Path.GetFileName(fullPath);
        RemoveStaleAtomicArtifacts(directory, leaf);
        var tempPath = Path.Combine(directory, $"{leaf}.{Guid.NewGuid():N}.tmp");
        var backupPath = Path.Combine(directory, $"{leaf}.{Guid.NewGuid():N}.bak");
        try
        {
            if (encoding is null)
            {
                File.WriteAllText(tempPath, contents);
            }
            else
            {
                File.WriteAllText(tempPath, contents, encoding);
            }

            InstallAtomicDestination(tempPath, fullPath, backupPath);
        }
        catch
        {
            TryDeleteAtomicArtifact(tempPath);
            TryDeleteAtomicArtifact(backupPath);

            throw;
        }
    }

    private static void RemoveStaleAtomicArtifacts(string directory, string leaf)
    {
        var cutoff = DateTime.UtcNow.AddDays(-1);
        try
        {
            foreach (var artifact in Directory.GetFiles(directory))
            {
                var name = Path.GetFileName(artifact);
                if (!name.StartsWith(leaf + ".", StringComparison.OrdinalIgnoreCase) ||
                    (!name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) && !name.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                DateTime lastWrite;
                try
                {
                    lastWrite = File.GetLastWriteTimeUtc(artifact);
                }
                catch (IOException)
                {
                    continue;
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }

                if (lastWrite < cutoff)
                {
                    TryDeleteAtomicArtifact(artifact);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"WARNING: Could not inspect atomic artifacts under {directory}: {ex.Message}");
        }
    }

    private static void InstallAtomicDestination(string tempPath, string fullPath, string backupPath)
    {
        const int maxRetries = 5;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                if (File.Exists(fullPath))
                {
                    File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
                    TryDeleteAtomicArtifact(backupPath);
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }

                return;
            }
            catch (IOException) when (attempt < maxRetries)
            {
                Thread.Sleep(10 * (attempt + 1));
            }
        }
    }

    private static void TryDeleteAtomicArtifact(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Preserve the report write result; stale artifacts are safe to retry later.
        }
    }

    public static void WriteSummaryJson(string path, string outputPath, IReadOnlyCollection<Finding> findings)
    {
        var summary = new
        {
            timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
            outputPath,
            counts = findings.GroupBy(x => x.Severity).ToDictionary(g => g.Key.ToString(), g => g.Count()),
            total = findings.Count
        };
        WriteTextAtomically(path, JsonSerializer.Serialize(summary, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
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

    public static string CreateAvailableReportRoot(string outputPath, DateTime timestamp)
    {
        var candidateTime = timestamp;
        while (true)
        {
            var candidate = Path.Combine(outputPath, candidateTime.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                candidateTime = candidateTime.AddSeconds(1);
                continue;
            }

            var reservation = Path.Combine(outputPath, $".report-reservation-{Guid.NewGuid():N}");
            Directory.CreateDirectory(reservation);
            try
            {
                Directory.Move(reservation, candidate);
                return candidate;
            }
            catch (IOException) when (Directory.Exists(candidate) || File.Exists(candidate))
            {
                TryDeleteReservation(reservation);
                candidateTime = candidateTime.AddSeconds(1);
            }
            catch
            {
                TryDeleteReservation(reservation);
                throw;
            }
        }
    }

    public static string CreateReportStagingRoot(string outputPath)
    {
        Directory.CreateDirectory(outputPath);
        while (true)
        {
            var candidate = Path.Combine(outputPath, $".report-staging-{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(candidate);
                return candidate;
            }
            catch (IOException) when (Directory.Exists(candidate) || File.Exists(candidate))
            {
                // A GUID collision is extraordinarily unlikely, but retry without exposing a partial build.
            }
        }
    }

    public static string PublishReport(string stagingPath, string outputPath, DateTime timestamp)
    {
        var fullStagingPath = Path.GetFullPath(stagingPath);
        var fullOutputPath = Path.GetFullPath(outputPath);
        var stagingParent = Path.GetDirectoryName(fullStagingPath)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var normalizedOutput = fullOutputPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.Equals(stagingParent, normalizedOutput, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Report staging must be directly under the output path.", nameof(stagingPath));
        }

        if (!Directory.Exists(fullStagingPath))
        {
            throw new DirectoryNotFoundException($"Report staging path does not exist: {stagingPath}");
        }

        var candidateTime = timestamp;
        while (true)
        {
            var candidate = Path.Combine(fullOutputPath, candidateTime.ToString("yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture));
            if (Directory.Exists(candidate) || File.Exists(candidate))
            {
                candidateTime = candidateTime.AddSeconds(1);
                continue;
            }

            try
            {
                UpdateSummaryOutputPath(fullStagingPath, candidate);
                Directory.Move(fullStagingPath, candidate);
                return candidate;
            }
            catch (IOException) when (Directory.Exists(candidate) || File.Exists(candidate))
            {
                candidateTime = candidateTime.AddSeconds(1);
            }
        }
    }

    private static void UpdateSummaryOutputPath(string stagingPath, string publishedPath)
    {
        var summaryPath = Path.Combine(stagingPath, "summary.json");
        if (!File.Exists(summaryPath))
        {
            return;
        }

        var summary = JsonNode.Parse(File.ReadAllText(summaryPath)) as JsonObject
            ?? throw new InvalidDataException($"Summary is not a JSON object: {summaryPath}");
        summary["outputPath"] = publishedPath;
        WriteTextAtomically(summaryPath, summary.ToJsonString(new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
    }

    public static void DiscardReportStaging(string stagingPath)
    {
        var fullPath = Path.GetFullPath(stagingPath);
        if (!Path.GetFileName(fullPath).StartsWith(".report-staging-", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Only report staging folders can be discarded.", nameof(stagingPath));
        }

        try
        {
            if (Directory.Exists(fullPath))
            {
                Directory.Delete(fullPath, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"WARNING: Could not discard report staging '{fullPath}': {ex.Message}");
        }
    }

    public static void CleanupOldSnapshots(string outputPath, int retentionDays)
    {
        if (!Directory.Exists(outputPath) || retentionDays <= 0)
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-1 * retentionDays);
        foreach (var dir in GetDirectories(outputPath, "snapshots"))
        {
            var name = Path.GetFileName(dir);
            if (name.Length >= 15 &&
                DateTime.TryParseExact(name[..15], "yyyy-MM-dd_HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) &&
                timestamp < cutoff)
            {
                TryDeleteDirectory(dir, "old snapshot", name);
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
        foreach (var dir in GetDirectories(outputPath, "collection staging folders"))
        {
            var name = Path.GetFileName(dir);
            const string prefix = "collection_";
            if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                !DateTime.TryParseExact(name[prefix.Length..], "yyyy-MM-dd_HHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var timestamp) ||
                timestamp >= cutoff)
            {
                continue;
            }

            TryDeleteDirectory(dir, "old collection staging", name);
        }
    }

    public static void CleanupOldMergeStaging(string outputPath, int retentionDays) =>
        CleanupOldGeneratedStaging(outputPath, retentionDays, "merged_raw_", "old merged raw staging");

    public static void CleanupOldReportReservations(string outputPath, int retentionDays) =>
        CleanupOldGeneratedStaging(outputPath, retentionDays, ".report-reservation-", "old report reservation");

    public static void CleanupOldReportStaging(string outputPath, int retentionDays) =>
        CleanupOldGeneratedStaging(outputPath, retentionDays, ".report-staging-", "old report staging");

    private static void CleanupOldGeneratedStaging(string outputPath, int retentionDays, string prefix, string description)
    {
        if (!Directory.Exists(outputPath) || retentionDays <= 0)
        {
            return;
        }

        var cutoff = DateTime.Now.AddDays(-1 * retentionDays);
        foreach (var dir in GetDirectories(outputPath, description))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                GetLastWriteTimeOrMax(dir) < cutoff)
            {
                TryDeleteDirectory(dir, description, name);
            }
        }
    }

    private static DateTime GetLastWriteTimeOrMax(string path)
    {
        try
        {
            return Directory.GetLastWriteTime(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTime.MaxValue;
        }
    }

    private static IReadOnlyCollection<string> GetDirectories(string outputPath, string description)
    {
        try
        {
            return Directory.GetDirectories(outputPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"WARNING: Could not inspect {description} under {outputPath}: {ex.Message}");
            return Array.Empty<string>();
        }
    }

    private static bool TryDeleteDirectory(string path, string description, string name)
    {
        try
        {
            Directory.Delete(path, recursive: true);
            Console.WriteLine($"Cleaned up {description}: {name}");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"WARNING: Could not clean up {description} '{name}': {ex.Message}");
            return false;
        }
    }

    private static void TryDeleteReservation(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch
        {
            // Preserve the original allocation failure; a stale reservation is harmless.
        }
    }

    public static void WriteBaselineConfigs(string configPath, IReadOnlyCollection<ServiceRecord> services, IReadOnlyCollection<TaskRecord> tasks, IReadOnlyCollection<SoftwareRecord> software, IReadOnlyCollection<DriverRecord> drivers)
    {
        Directory.CreateDirectory(configPath);
        var json = new JsonSerializerOptions { WriteIndented = true };

        var servicesConfig = new ExpectedServicesConfig(services.OrderBy(x => x.Server).ThenBy(x => x.Name).Select(x => new ExpectedService(x.Server, x.Name, x.Status, x.StartupType, "Critical")).ToList());
        WriteTextAtomically(Path.Combine(configPath, "expected_services.json"), JsonSerializer.Serialize(servicesConfig, json));

        var tasksConfig = new ExpectedTasksConfig(tasks.OrderBy(x => x.Server).ThenBy(x => x.TaskPath).ThenBy(x => x.TaskName).Select(x => new ExpectedTask(x.Server, x.TaskPath, x.TaskName, x.Enabled)).ToList());
        WriteTextAtomically(Path.Combine(configPath, "expected_tasks.json"), JsonSerializer.Serialize(tasksConfig, json));

        var softwareConfig = new ExpectedSoftwareConfig(software.Where(x => !string.IsNullOrWhiteSpace(x.Version)).OrderBy(x => x.Server).ThenBy(x => x.Name).Select(x => new ExpectedSoftware(x.Server, x.Name, x.Version ?? "")).ToList());
        WriteTextAtomically(Path.Combine(configPath, "expected_software.json"), JsonSerializer.Serialize(softwareConfig, json));

        var driversConfig = new ExpectedDriversConfig(drivers.Where(x => !string.IsNullOrWhiteSpace(x.Version)).OrderBy(x => x.Server).ThenBy(x => x.Type).ThenBy(x => x.Name).ThenBy(x => x.Architecture).Select(x => new ExpectedDriver(x.Server, x.Type, x.Name, x.Architecture, x.Version ?? "")).ToList());
        WriteTextAtomically(Path.Combine(configPath, "expected_drivers.json"), JsonSerializer.Serialize(driversConfig, json));
    }
}
