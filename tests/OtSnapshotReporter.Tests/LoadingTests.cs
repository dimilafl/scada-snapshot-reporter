using System.Text.Json;

namespace OtSnapshotReporter.Tests;

public sealed class LoadingTests
{
    [Fact]
    public void LoadServersConfig_NormalizesNamesAndRejectsUnusableEntries()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-servers-config-test-" + Guid.NewGuid());
        var path = Path.Combine(root, "servers.json");
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(path, "{\"servers\":[{\"name\":\" server-a \",\"roles\":[]},{\"name\":\"SERVER-A\"},{\"name\":\"server-b\"},{\"name\":\"\"},null]}");

            var config = Loading.LoadServersConfig(path, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Equal(new[] { "server-a", "server-b" }, config.Servers.Select(server => server.Name));
            File.WriteAllText(path, "{\"servers\":null}");
            var exception = Assert.Throws<InvalidDataException>(() => Loading.LoadServersConfig(path, new JsonSerializerOptions()));
            Assert.Contains("servers.json", exception.Message);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadConfig_RejectsNullOrScalarCollections()
    {
        var path = Path.Combine(Path.GetTempPath(), "ot-config-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{\"tasks\":null}");
            var nullException = Assert.Throws<InvalidDataException>(() => Loading.LoadConfig<ExpectedTasksConfig>(path, new JsonSerializerOptions(), "tasks"));
            Assert.Contains("tasks", nullException.Message);

            File.WriteAllText(path, "{\"tasks\":\"invalid\"}");
            var scalarException = Assert.Throws<InvalidDataException>(() => Loading.LoadConfig<ExpectedTasksConfig>(path, new JsonSerializerOptions(), "tasks"));
            Assert.Contains("array", scalarException.Message);

            File.WriteAllText(path, "{\"tasks\":[null]}");
            var itemException = Assert.Throws<InvalidDataException>(() => Loading.LoadConfig<ExpectedTasksConfig>(path, new JsonSerializerOptions(), "tasks"));
            Assert.Contains("only objects", itemException.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadMaintenanceWindowsConfig_RejectsInvalidIntervals()
    {
        var path = Path.Combine(Path.GetTempPath(), "ot-maintenance-" + Guid.NewGuid() + ".json");
        try
        {
            File.WriteAllText(path, "{\"windows\":[{\"name\":\"Patch\",\"start\":\"2026-07-02T00:00:00\",\"end\":\"2026-07-01T00:00:00\"}]}");

            var exception = Assert.Throws<InvalidDataException>(() => Loading.LoadMaintenanceWindowsConfig(path, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));

            Assert.Contains("ends before it starts", exception.Message);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void LoadJson_FileNotFound_ReturnsNull()
    {
        var result = Loading.LoadJson<ServiceRecord>(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json"), new JsonSerializerOptions());
        Assert.Null(result);
    }

    [Fact]
    public void LoadJson_CorruptFile_ReturnsNull()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "this is not json");
        var result = Loading.LoadJson<ServiceRecord>(path, new JsonSerializerOptions());
        File.Delete(path);
        Assert.Null(result);
    }

    [Fact]
    public void LoadJson_EmptyArray_ReturnsEmptyList()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "[]");
        var result = Loading.LoadJson<List<ServiceRecord>>(path, new JsonSerializerOptions());
        File.Delete(path);
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void LoadJson_ValidServiceArray_ReturnsDeserialized()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, """[{"Server":"SRV01","Name":"EventLog","DisplayName":"Event Log","Status":"Running","StartupType":"Automatic","StartName":"LocalSystem"}]""");
        var result = Loading.LoadJson<List<ServiceRecord>>(path, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        File.Delete(path);
        Assert.NotNull(result);
        Assert.Single(result);
    }

    [Fact]
    public void LoadRecords_FiltersRowsThatFailTheRequiredFieldPredicate()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, "[{\"Server\":\"SRV01\",\"Name\":\"EventLog\",\"DisplayName\":\"Event Log\",\"Status\":\"Running\",\"StartupType\":\"Automatic\",\"StartName\":\"LocalSystem\"},{\"Server\":\"SRV01\",\"Name\":\"\"}]");

        var result = Loading.LoadRecords<ServiceRecord>(
            path,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            record => !string.IsNullOrWhiteSpace(record.Server) && !string.IsNullOrWhiteSpace(record.Name),
            "service");

        File.Delete(path);
        var record = Assert.Single(result);
        Assert.Equal("EventLog", record.Name);
    }

    [Fact]
    public void LoadRecords_KeepsValidRowsWhenOneRowHasInvalidField()
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(
            path,
            "[{\"Server\":\"SRV01\",\"Drive\":\"C:\",\"TotalGb\":100,\"FreeGb\":50,\"FreePercent\":50},{\"Server\":\"SRV01\",\"Drive\":\"D:\",\"TotalGb\":200,\"FreeGb\":\"not-a-number\",\"FreePercent\":20}]");

        var result = Loading.LoadRecords<DiskRecord>(
            path,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true },
            record => !string.IsNullOrWhiteSpace(record.Server) && !string.IsNullOrWhiteSpace(record.Drive),
            "disk");

        File.Delete(path);
        var record = Assert.Single(result);
        Assert.Equal("C:", record.Drive);
    }

    [Fact]
    public void CopyRawInputs_ContinuesWhenOneFileIsLocked()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-copy-test-" + Guid.NewGuid());
        FileStream? blocker = null;
        try
        {
            var source = Path.Combine(root, "source");
            var destination = Path.Combine(root, "destination");
            Directory.CreateDirectory(source);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(source, "usable.json"), "[]");
            var lockedPath = Path.Combine(source, "locked.json");
            File.WriteAllText(lockedPath, "[]");
            blocker = new FileStream(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None);

            Loading.CopyRawInputs(source, destination);

            Assert.True(File.Exists(Path.Combine(destination, "usable.json")));
            Assert.False(File.Exists(Path.Combine(destination, "locked.json")));
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
    public void ResolveRawRoot_EmptyDirectory_ReturnsDefaultRawPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var result = Loading.ResolveRawRoot(dir, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Directory.Delete(dir);
        Assert.Equal(Path.Combine(dir, "raw"), result);
    }

    [Fact]
    public void LoadPreviousSnapshot_AcceptsDirectRawFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-previous-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "services.json"), "[{\"Server\":\"SRV01\",\"Name\":\"Demo\",\"DisplayName\":\"Demo\",\"Status\":\"Running\",\"StartupType\":\"Automatic\",\"StartName\":\"SYSTEM\"}]");

            var previous = Loading.LoadPreviousSnapshot(root, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Single(previous.Services);
            Assert.Equal("SRV01", previous.Services.Single().Server);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadPreviousSnapshot_MergesPerServerFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-previous-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "server-a", "raw"));
            Directory.CreateDirectory(Path.Combine(root, "server-b", "raw"));
            var record = "[{\"Server\":\"{0}\",\"Name\":\"Demo\",\"DisplayName\":\"Demo\",\"Status\":\"Running\",\"StartupType\":\"Automatic\",\"StartName\":\"SYSTEM\"}]";
            File.WriteAllText(Path.Combine(root, "server-a", "raw", "services.json"), record.Replace("{0}", "server-a", StringComparison.Ordinal));
            File.WriteAllText(Path.Combine(root, "server-b", "raw", "services.json"), record.Replace("{0}", "server-b", StringComparison.Ordinal));

            var previous = Loading.LoadPreviousSnapshot(root, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Equal(2, previous.Services.Count);
            var servers = previous.Services.Select(x => x.Server).OrderBy(x => x).ToArray();
            Assert.Equal("server-a", servers[0]);
            Assert.Equal("server-b", servers[1]);
            Assert.Empty(Directory.GetDirectories(root, ".*", SearchOption.TopDirectoryOnly));
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadPreviousSnapshot_UsesLatestNestedPerServerFolders()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-previous-nested-" + Guid.NewGuid());
        try
        {
            var oldRaw = Path.Combine(root, "server-a", "2026-01-01_010000", "raw");
            var latestRaw = Path.Combine(root, "server-a", "2026-01-02_010000", "raw");
            Directory.CreateDirectory(oldRaw);
            Directory.CreateDirectory(latestRaw);
            File.WriteAllText(
                Path.Combine(oldRaw, "services.json"),
                "[{\"Server\":\"server-a\",\"Name\":\"Old\",\"DisplayName\":\"Old\",\"Status\":\"Running\",\"StartupType\":\"Automatic\",\"StartName\":\"SYSTEM\"}]");
            File.WriteAllText(
                Path.Combine(latestRaw, "services.json"),
                "[{\"Server\":\"server-a\",\"Name\":\"Latest\",\"DisplayName\":\"Latest\",\"Status\":\"Running\",\"StartupType\":\"Automatic\",\"StartName\":\"SYSTEM\"}]");

            var previous = Loading.LoadPreviousSnapshot(root, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var service = Assert.Single(previous.Services);
            Assert.Equal("Latest", service.Name);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void LoadPreviousSnapshot_LoadsAndFiltersPhase3Modules()
    {
        var root = Path.Combine(Path.GetTempPath(), "ot-previous-phase3-" + Guid.NewGuid());
        try
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "odbc_dsn_tests.json"), "[{\"server\":\"SRV01\",\"dsnName\":\"Demo\",\"driverName\":\"Driver\",\"type\":\"System\",\"architecture\":\"64-bit\",\"connectionPassed\":true},{\"server\":\"SRV01\",\"dsnName\":\"Invalid\",\"driverName\":\"\",\"type\":\"System\",\"architecture\":\"64-bit\",\"connectionPassed\":false}]");
            File.WriteAllText(Path.Combine(root, "certificates.json"), "[{\"server\":\"SRV01\",\"subject\":\"CN=Demo\",\"issuer\":\"CA\",\"thumbprint\":\"abc\",\"notBefore\":\"2026\",\"notAfter\":\"2027\",\"daysUntilExpiry\":100,\"store\":\"My\"},{\"server\":\"SRV01\",\"subject\":\"CN=Invalid\",\"issuer\":\"CA\",\"thumbprint\":\"\",\"notBefore\":\"2026\",\"notAfter\":\"2027\",\"daysUntilExpiry\":100,\"store\":\"My\"}]");
            File.WriteAllText(Path.Combine(root, "sql_agent_jobs.json"), "[{\"server\":\"SRV01\",\"instance\":\".\",\"jobName\":\"Nightly\",\"enabled\":true,\"lastRunStatus\":1},{\"server\":\"SRV01\",\"instance\":\"\",\"jobName\":\"Invalid\",\"enabled\":true,\"lastRunStatus\":1}]");
            File.WriteAllText(Path.Combine(root, "ssrs_subscriptions.json"), "[{\"server\":\"SRV01\",\"instance\":\".\",\"reportPath\":\"/Reports/Daily\",\"subscriptionDescription\":\"Daily\",\"owner\":\"operator\",\"ownerExists\":true,\"lastStatus\":\"Done\",\"enabled\":true},{\"server\":\"SRV01\",\"instance\":\".\",\"reportPath\":\"/Reports/Invalid\",\"subscriptionDescription\":\"\",\"owner\":\"operator\",\"ownerExists\":true,\"lastStatus\":\"Done\",\"enabled\":true}]");

            var previous = Loading.LoadPreviousSnapshot(root, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            Assert.Single(previous.OdbcDsns);
            Assert.Single(previous.Certificates);
            Assert.Single(previous.SqlAgentJobs);
            Assert.Single(previous.SsrsSubscriptions);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }
}
