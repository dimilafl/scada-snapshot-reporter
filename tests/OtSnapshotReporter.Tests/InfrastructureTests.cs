using System.Text.Json;

namespace OtSnapshotReporter.Tests;

public sealed class InfrastructureTests
{
    [Fact] public void Key_TwoArguments_ReturnsJoined() => Assert.Equal("a|b", Helpers.Key("a", "b"));
    [Fact] public void Key_ThreeArguments_ReturnsJoined() => Assert.Equal("a|b|c", Helpers.Key("a", "b", "c"));
    [Fact] public void Key_TrimsWhitespaceAndNormalizesNulls() => Assert.Equal("a||c", Helpers.Key(" a ", null, " c "));
    [Fact] public void EqualsText_NullVsNull_ReturnsTrue() => Assert.True(Helpers.EqualsText(null, null));
    [Fact] public void EqualsText_NullVsEmpty_ReturnsTrue() => Assert.True(Helpers.EqualsText(null, ""));
    [Fact] public void EqualsText_SameString_ReturnsTrue() => Assert.True(Helpers.EqualsText("ABC", "abc"));
    [Fact] public void EqualsText_DifferentString_ReturnsFalse() => Assert.False(Helpers.EqualsText("ABC", "def"));
    [Fact] public void ParseSeverity_Critical_ReturnsCritical() => Assert.Equal(Severity.Critical, Helpers.ParseSeverity("Critical", Severity.Info));
    [Fact] public void ParseSeverity_Unknown_ReturnsInfo() => Assert.Equal(Severity.Info, Helpers.ParseSeverity("nope", Severity.Info));
    [Fact] public void ParseSeverity_UndefinedNumeric_ReturnsFallback() => Assert.Equal(Severity.Info, Helpers.ParseSeverity("99", Severity.Info));

    [Fact] public void TryParseTimestamp_Iso8601_IsCultureIndependent()
    {
        Assert.True(Helpers.TryParseTimestamp("2026-06-03T06:00:00Z", out var parsed));
        Assert.Equal(2026, parsed.Year);
        Assert.Equal(6, parsed.Month);
        Assert.Equal(3, parsed.Day);
    }

    [Fact] public void TryParseTimestamp_InvalidValue_ReturnsFalse()
    {
        Assert.False(Helpers.TryParseTimestamp("not-a-timestamp", out _));
    }

    [Fact] public void LoadJson_ValidFile_ReturnsDeserialized()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json");
        File.WriteAllText(path, "{\"disk_free_percent_warning\":20}");
        var thresholds = Loading.LoadJson<Thresholds>(path, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.Equal(20, thresholds?.DiskFreePercentWarning);
    }

    [Fact] public void LoadJson_MissingFile_ReturnsNull()
    {
        Assert.Null(Loading.LoadJson<Thresholds>(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), new JsonSerializerOptions()));
    }

    [Fact]
    public void GetAvailableReportRoot_SkipsExistingFoldersAndFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-report-root-test-" + Guid.NewGuid());
        var timestamp = new DateTime(2026, 7, 16, 12, 34, 56);
        var first = Path.Combine(root, "2026-07-16_123456");
        var second = Path.Combine(root, "2026-07-16_123457");
        try
        {
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(root);
            File.WriteAllText(second, "reserved");

            var result = Writing.GetAvailableReportRoot(root, timestamp);

            Assert.Equal(Path.Combine(root, "2026-07-16_123458"), result);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CreateAvailableReportRoot_ReservesFolderAndSkipsExistingFoldersAndFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-report-reservation-test-" + Guid.NewGuid());
        var timestamp = new DateTime(2026, 7, 16, 12, 34, 56);
        var first = Path.Combine(root, "2026-07-16_123456");
        var second = Path.Combine(root, "2026-07-16_123457");
        try
        {
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(root);
            File.WriteAllText(second, "reserved");

            var result = Writing.CreateAvailableReportRoot(root, timestamp);

            Assert.Equal(Path.Combine(root, "2026-07-16_123458"), result);
            Assert.True(Directory.Exists(result));
            Assert.Empty(Directory.GetFileSystemEntries(result));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public async Task CreateAvailableReportRoot_ConcurrentCallsReturnDistinctFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-report-concurrency-test-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            var timestamp = new DateTime(2026, 7, 16, 12, 34, 56);
            var results = await Task.WhenAll(Enumerable.Range(0, 8)
                .Select(_ => Task.Run(() => Writing.CreateAvailableReportRoot(root, timestamp))));

            Assert.Equal(results.Length, results.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(results, result => Assert.True(Directory.Exists(result)));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteTextAtomically_ReplacesContentAndRemovesTemporaryFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-atomic-write-test-" + Guid.NewGuid());
        var path = Path.Combine(root, "report.txt");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "old");

            Writing.WriteTextAtomically(path, "new");

            Assert.Equal("new", File.ReadAllText(path));
            Assert.False(File.Exists(path + ".tmp"));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void WriteTextAtomically_CleansStaleArtifactsAndPreservesRecentOnes()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-atomic-retention-test-" + Guid.NewGuid());
        var path = Path.Combine(root, "report.txt");
        var staleTemp = path + ".old.tmp";
        var staleBackup = path + ".old.bak";
        var recentTemp = path + ".recent.tmp";
        var recentBackup = path + ".recent.bak";
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "old");
            File.WriteAllText(staleTemp, "stale");
            File.WriteAllText(staleBackup, "stale");
            File.WriteAllText(recentTemp, "recent");
            File.WriteAllText(recentBackup, "recent");
            File.SetLastWriteTimeUtc(staleTemp, DateTime.UtcNow.AddDays(-2));
            File.SetLastWriteTimeUtc(staleBackup, DateTime.UtcNow.AddDays(-2));

            Writing.WriteTextAtomically(path, "new");

            Assert.Equal("new", File.ReadAllText(path));
            Assert.False(File.Exists(staleTemp));
            Assert.False(File.Exists(staleBackup));
            Assert.True(File.Exists(recentTemp));
            Assert.True(File.Exists(recentBackup));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task WriteTextAtomically_ConcurrentWritesLeaveOneValidDocument()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-atomic-concurrency-test-" + Guid.NewGuid());
        var path = Path.Combine(root, "report.txt");
        var values = Enumerable.Range(0, 12).Select(index => $"value-{index}").ToArray();
        try
        {
            Directory.CreateDirectory(root);
            await Task.WhenAll(values.Select(value => Task.Run(() => Writing.WriteTextAtomically(path, value))));

            Assert.Contains(File.ReadAllText(path), values);
            Assert.DoesNotContain(Directory.GetFiles(root), file =>
                file.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void CleanupOldSnapshots_ContinuesWhenOneFolderIsLocked()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-retention-test-" + Guid.NewGuid());
        FileStream? blocker = null;
        try
        {
            var locked = Path.Combine(root, "2001-01-01_0000", "snapshot.json");
            var removable = Path.Combine(root, "2001-01-02_0000");
            Directory.CreateDirectory(Path.GetDirectoryName(locked)!);
            Directory.CreateDirectory(removable);
            blocker = new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            Writing.CleanupOldSnapshots(root, retentionDays: 1);

            Assert.True(Directory.Exists(Path.GetDirectoryName(locked)));
            Assert.False(Directory.Exists(removable));
        }
        finally
        {
            blocker?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CleanupOldCollectionStaging_ContinuesWhenOneFolderIsLocked()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-staging-retention-test-" + Guid.NewGuid());
        FileStream? blocker = null;
        try
        {
            var locked = Path.Combine(root, "collection_2001-01-01_000000", "snapshot.json");
            var removable = Path.Combine(root, "collection_2001-01-02_000000");
            Directory.CreateDirectory(Path.GetDirectoryName(locked)!);
            Directory.CreateDirectory(removable);
            blocker = new FileStream(locked, FileMode.Create, FileAccess.ReadWrite, FileShare.None);

            Writing.CleanupOldCollectionStaging(root, retentionDays: 1);

            Assert.True(Directory.Exists(Path.GetDirectoryName(locked)));
            Assert.False(Directory.Exists(removable));
        }
        finally
        {
            blocker?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CleanupOldMergeStaging_RemovesOldFallbackAndKeepsRecent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-merge-retention-test-" + Guid.NewGuid());
        try
        {
            var oldPath = Path.Combine(root, "merged_raw_old");
            var recentPath = Path.Combine(root, "merged_raw_recent");
            Directory.CreateDirectory(oldPath);
            Directory.CreateDirectory(recentPath);
            Directory.SetLastWriteTime(oldPath, DateTime.Now.AddDays(-10));

            Writing.CleanupOldMergeStaging(root, retentionDays: 1);

            Assert.False(Directory.Exists(oldPath));
            Assert.True(Directory.Exists(recentPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void CleanupOldReportReservations_RemovesOldReservationsAndKeepsRecent()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-reservation-retention-test-" + Guid.NewGuid());
        try
        {
            var oldPath = Path.Combine(root, ".report-reservation-old");
            var recentPath = Path.Combine(root, ".report-reservation-recent");
            Directory.CreateDirectory(oldPath);
            Directory.CreateDirectory(recentPath);
            Directory.SetLastWriteTime(oldPath, DateTime.Now.AddDays(-10));

            Writing.CleanupOldReportReservations(root, retentionDays: 1);

            Assert.False(Directory.Exists(oldPath));
            Assert.True(Directory.Exists(recentPath));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact] public void AppOptions_Help_ReturnsHelpRequest()
    {
        var options = AppOptions.Parse(["--help"]);

        Assert.True(options.HelpRequested);
    }

    [Fact] public void AppOptions_ParsesAllSupportedValues()
    {
        var options = AppOptions.Parse([
            "--input", "input",
            "--config", "config",
            "--output", "output",
            "--previous", "previous",
            "--accept-baseline"]);

        Assert.Equal("input", options.InputPath);
        Assert.Equal("config", options.ConfigPath);
        Assert.Equal("output", options.OutputPath);
        Assert.Equal("previous", options.PreviousPath);
        Assert.True(options.AcceptBaseline);
        Assert.False(options.HelpRequested);
    }

    [Fact] public void AppOptions_MissingValue_ThrowsHelpfulError()
    {
        var exception = Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--input"]));

        Assert.Contains("--input requires a value", exception.Message);
    }

    [Fact] public void AppOptions_UnknownOption_ThrowsHelpfulError()
    {
        var exception = Assert.Throws<ArgumentException>(() => AppOptions.Parse(["--unknown"]));

        Assert.Contains("Unknown option", exception.Message);
    }

    [Fact] public void ResolveRawRoot_MergesValidFilesAndRecordsCorruptFiles()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-snapshot-test-" + Guid.NewGuid());
        var input = Path.Combine(root, "input");
        var output = Path.Combine(root, "output");
        try
        {
            Directory.CreateDirectory(Path.Combine(input, "server-a", "raw"));
            Directory.CreateDirectory(Path.Combine(input, "server-b", "raw"));
            File.WriteAllText(Path.Combine(input, "server-a", "raw", "services.json"), "[{\"server\":\"server-a\",\"name\":\"Demo\"}]");
            File.WriteAllText(Path.Combine(input, "server-b", "raw", "services.json"), "not json");

            var merged = Loading.ResolveRawRoot(input, output);

            var services = JsonDocument.Parse(File.ReadAllText(Path.Combine(merged, "services.json"))).RootElement;
            var errors = JsonDocument.Parse(File.ReadAllText(Path.Combine(merged, "_errors.json"))).RootElement;
            Assert.Equal(1, services.GetArrayLength());
            Assert.Equal("server-a", services[0].GetProperty("server").GetString());
            Assert.Contains(errors.EnumerateArray(), item => item.GetProperty("server").GetString() == "server-b");
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveRawRoot_UsesFallbackWhenMergedStagingIsLocked()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-locked-merge-test-" + Guid.NewGuid());
        FileStream? blocker = null;
        try
        {
            var input = Path.Combine(root, "input", "server-a", "raw");
            var output = Path.Combine(root, "output");
            var stale = Path.Combine(output, "merged_raw", "stale.json");
            Directory.CreateDirectory(input);
            Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
            File.WriteAllText(Path.Combine(input, "services.json"), "[{\"server\":\"server-a\",\"name\":\"Current\"}]");
            File.WriteAllText(stale, "[]");
            blocker = new FileStream(stale, FileMode.Open, FileAccess.Read, FileShare.None);

            var merged = Loading.ResolveRawRoot(Path.Combine(root, "input"), output);

            Assert.NotEqual(Path.Combine(output, "merged_raw"), merged);
            Assert.True(File.Exists(Path.Combine(merged, "services.json")));
        }
        finally
        {
            blocker?.Dispose();
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveRawRoot_MergesLatestNestedPerServerFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-nested-snapshot-test-" + Guid.NewGuid());
        var input = Path.Combine(root, "input");
        var output = Path.Combine(root, "output");
        try
        {
            var oldRaw = Path.Combine(input, "server-a", "2026-01-01_010000", "raw");
            var latestRaw = Path.Combine(input, "server-a", "2026-01-02_010000", "raw");
            var serverBRaw = Path.Combine(input, "server-b", "2026-01-03_010000", "raw");
            var reportRaw = Path.Combine(input, "Output", "2026-01-04_010000", "raw");
            Directory.CreateDirectory(oldRaw);
            Directory.CreateDirectory(latestRaw);
            Directory.CreateDirectory(serverBRaw);
            Directory.CreateDirectory(reportRaw);
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(reportRaw)!, "index.html"), "<html></html>");

            const string serviceTemplate = "[{{\"server\":\"{0}\",\"name\":\"{1}\",\"displayName\":\"{1}\",\"status\":\"Running\",\"startupType\":\"Automatic\",\"startName\":\"SYSTEM\"}}]";
            File.WriteAllText(Path.Combine(oldRaw, "services.json"), string.Format(serviceTemplate, "server-a", "Old"));
            File.WriteAllText(Path.Combine(latestRaw, "services.json"), string.Format(serviceTemplate, "server-a", "Latest"));
            File.WriteAllText(Path.Combine(serverBRaw, "services.json"), string.Format(serviceTemplate, "server-b", "Current"));
            File.WriteAllText(Path.Combine(reportRaw, "services.json"), string.Format(serviceTemplate, "localhost", "Report"));

            var merged = Loading.ResolveRawRoot(input, output);

            using var services = JsonDocument.Parse(File.ReadAllText(Path.Combine(merged, "services.json")));
            var names = services.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("name").GetString())
                .OrderBy(name => name)
                .ToArray();
            Assert.Equal(new[] { "Current", "Latest" }, names);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveRawRoot_IgnoresNonTimestampNestedRawFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-nested-layout-test-" + Guid.NewGuid());
        var input = Path.Combine(root, "input");
        var output = Path.Combine(root, "output");
        try
        {
            var unrelatedRaw = Path.Combine(input, "archive", "old-run", "raw");
            Directory.CreateDirectory(unrelatedRaw);
            File.WriteAllText(
                Path.Combine(unrelatedRaw, "services.json"),
                "[{\"server\":\"archive\",\"name\":\"ShouldNotBeLoaded\"}]");

            var merged = Loading.ResolveRawRoot(input, output);

            Assert.Equal(Path.Combine(input, "raw"), merged);
            Assert.False(Directory.Exists(Path.Combine(output, "merged_raw")));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void ResolveRawRoot_IgnoresTopLevelReportsAndCollectionStaging()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-top-level-layout-test-" + Guid.NewGuid());
        var input = Path.Combine(root, "input");
        var output = Path.Combine(root, "output");
        try
        {
            var serverRaw = Path.Combine(input, "server-a", "raw");
            var reportRaw = Path.Combine(input, "2026-01-01_010000", "raw");
            var collectionRaw = Path.Combine(input, "collection_2026-01-02_010000", "raw");
            Directory.CreateDirectory(serverRaw);
            Directory.CreateDirectory(reportRaw);
            Directory.CreateDirectory(collectionRaw);
            File.WriteAllText(Path.Combine(input, "2026-01-01_010000", "index.html"), "<html></html>");

            const string serviceTemplate = "[{{\"server\":\"{0}\",\"name\":\"{1}\"}}]";
            File.WriteAllText(Path.Combine(serverRaw, "services.json"), string.Format(serviceTemplate, "server-a", "Current"));
            File.WriteAllText(Path.Combine(reportRaw, "services.json"), string.Format(serviceTemplate, "report", "ShouldNotLoad"));
            File.WriteAllText(Path.Combine(collectionRaw, "services.json"), string.Format(serviceTemplate, "collection", "ShouldNotLoad"));

            var merged = Loading.ResolveRawRoot(input, output);

            using var services = JsonDocument.Parse(File.ReadAllText(Path.Combine(merged, "services.json")));
            var names = services.RootElement.EnumerateArray()
                .Select(item => item.GetProperty("name").GetString())
                .ToArray();
            Assert.Equal(new[] { "Current" }, names);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
