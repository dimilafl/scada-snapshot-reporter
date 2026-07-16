namespace OtSnapshotReporter.Tests;

public sealed class AnalyzerTests
{
    [Fact] public void AnalyzeServices_StoppedExpectedService_ProducesCritical()
    {
        var findings = Analyzers.AnalyzeServices([new("SRV01", "EventLog", "Windows Event Log", "Stopped", "Automatic", "LocalSystem")], new ExpectedServicesConfig([new("SRV01", "EventLog", "Running", "Automatic", "Critical")])).ToList();
        Assert.Contains(findings, x => x.Module == "services" && x.Severity == Severity.Critical);
    }

    [Fact] public void AnalyzeServices_MissingExpectedService_ProducesHigh()
    {
        var findings = Analyzers.AnalyzeServices([], new ExpectedServicesConfig([new("SRV01", "EventLog", "Running", "Automatic", "Critical")])).ToList();
        Assert.Contains(findings, x => x.Message.Contains("missing"));
    }

    [Fact] public void AnalyzeServices_AllMatching_ProducesNoFindings()
    {
        var findings = Analyzers.AnalyzeServices([new("SRV01", "EventLog", "Windows Event Log", "Running", "Automatic", "LocalSystem")], new ExpectedServicesConfig([new("SRV01", "EventLog", "Running", "Automatic", "Critical")])).ToList();
        Assert.Empty(findings);
    }

    [Fact] public void AnalyzeDisks_BelowCritical_ProducesCritical()
    {
        var findings = Analyzers.AnalyzeDisks([new("SRV01", "C:", 100, 5, 5)], new Thresholds()).ToList();
        Assert.Contains(findings, x => x.Severity == Severity.Critical);
    }

    [Fact] public void AnalyzeDisks_BelowWarning_ProducesMedium()
    {
        var findings = Analyzers.AnalyzeDisks([new("SRV01", "C:", 100, 12, 12)], new Thresholds()).ToList();
        Assert.Contains(findings, x => x.Severity == Severity.Medium);
    }

    [Fact] public void AnalyzeDisks_AboveThreshold_ProducesNoFindings()
    {
        Assert.Empty(Analyzers.AnalyzeDisks([new("SRV01", "C:", 100, 50, 50)], new Thresholds()));
    }

    [Fact] public void AnalyzeDisks_NegativeFreePercent_Ignores()
    {
        Assert.Empty(Analyzers.AnalyzeDisks([new("SRV01", "C:", 100, 200, -10)], new Thresholds()));
    }

    [Fact] public void AnalyzeServices_EmptyRecordsAndConfig_ProducesNoFindings()
    {
        Assert.Empty(Analyzers.AnalyzeServices([], new ExpectedServicesConfig()));
    }

    [Fact] public void AnalyzeServices_NoExpectedServices_ProducesNoFindings()
    {
        Assert.Empty(Analyzers.AnalyzeServices([new("SRV01", "Foo", "Foo", "Running", "Automatic", "SYSTEM")], new ExpectedServicesConfig()));
    }

    [Fact] public void AnalyzeMissingServers_MissingConfiguredServer_ProducesCritical()
    {
        var configured = new ServersConfig([new("SRV01", []), new("SRV02", [])]);

        var findings = Analyzers.AnalyzeMissingServers(configured, ["SRV01"]).ToList();

        var finding = Assert.Single(findings);
        Assert.Equal("SRV02", finding.Server);
        Assert.Equal("collection_errors", finding.Module);
        Assert.Equal(Severity.Critical, finding.Severity);
        Assert.Contains("No collector data", finding.Message);
    }

    [Fact] public void AnalyzeMissingServers_AllConfiguredServersObserved_ProducesNoFindings()
    {
        var configured = new ServersConfig([new("SRV01", [])]);

        Assert.Empty(Analyzers.AnalyzeMissingServers(configured, ["srv01"]));
    }

    [Fact] public void AnalyzeTasks_UnexpectedTaskWithoutFailure_ProducesNoFinding()
    {
        var task = new TaskRecord("SRV01", "\\Ops\\", "Extra", true, "Ready", DateTime.Now.ToString("s"), 0, null, "SYSTEM", "cmd.exe");
        Assert.Empty(Analyzers.AnalyzeTasks([task], new ExpectedTasksConfig(), new Thresholds()));
    }

    [Fact] public void AnalyzeTasks_ExpectedDisabledMismatch_ProducesHigh()
    {
        var task = new TaskRecord("SRV01", "\\Ops\\", "Export", false, "Disabled", DateTime.Now.ToString("s"), 0, null, "SYSTEM", "cmd.exe");
        var findings = Analyzers.AnalyzeTasks([task], new ExpectedTasksConfig([new("SRV01", "\\Ops\\", "Export", true)]), new Thresholds()).ToList();
        Assert.Contains(findings, x => x.Severity == Severity.High);
    }

    [Fact] public void AnalyzeTasks_LastRunResultNonZero_ProducesHigh()
    {
        var task = new TaskRecord("SRV01", "\\Ops\\", "Export", true, "Ready", DateTime.Now.ToString("s"), 1, null, "SYSTEM", "cmd.exe");
        Assert.Contains(Analyzers.AnalyzeTasks([task], new ExpectedTasksConfig(), new Thresholds()), x => x.Message.Contains("Last task result"));
    }

    [Fact] public void AnalyzeTasks_UsesExplicitEvaluationTimeForStaleness()
    {
        var task = new TaskRecord("SRV01", "\\Ops\\", "Export", true, "Ready", "2026-06-01T00:00:00", 0, null, "SYSTEM", "cmd.exe");

        var findings = Analyzers.AnalyzeTasks(
            [task],
            new ExpectedTasksConfig(),
            new Thresholds { TaskNotRunHoursWarning = 24 },
            new DateTime(2026, 6, 3, 0, 0, 0)).ToList();

        Assert.Contains(findings, x => x.Message.Contains("Task has not run"));
    }

    [Fact] public void AnalyzeUptime_UnexpectedReboot_ProducesHigh()
    {
        var previous = new PreviousSnapshot([], [], [], [new("SRV01", "2026-01-01T01:00:00", 1)], [], [], [], [], []);
        Assert.Contains(Analyzers.AnalyzeUptime([new("SRV01", "2026-01-02T01:00:00", 1)], new Thresholds(), previous), x => x.Severity == Severity.High);
    }

    [Fact] public void AnalyzeSoftware_WrongVersion_ProducesHigh()
    {
        Assert.Contains(Analyzers.AnalyzeSoftware([new("SRV01", "DemoDB", "7", "Microsoft", null)], new ExpectedSoftwareConfig([new("SRV01", "DemoDB", "8")])), x => x.Message.Contains("expected 8"));
    }

    [Fact] public void AnalyzeDrivers_MissingDriver_ProducesHigh()
    {
        Assert.Contains(Analyzers.AnalyzeDrivers([], new ExpectedDriversConfig([new("SRV01", "ODBC", "SQL Server", "64-bit", "1")])), x => x.Message.Contains("missing"));
    }

    [Fact] public void AnalyzeEventLogs_CriticalEvent_ProducesHigh()
    {
        Assert.Contains(Analyzers.AnalyzeEventLogs([new("SRV01", "System", "Disk", 1, 1, null, 7, 24)]), x => x.Message.Contains("critical"));
    }

    [Fact] public void AnalyzeEventLogs_ZeroInfo_ProducesNoFindings()
    {
        Assert.Empty(Analyzers.AnalyzeEventLogs([new("SRV01", "System", "Info", 4, 0, null, null, 24)]));
    }

    [Fact] public void AnalyzeFileShares_Unreachable_ProducesHigh()
    {
        Assert.Contains(Analyzers.AnalyzeFileShares([new("SRV01", "Archive", "\\\\s\\x", false, "No route", null)]), x => x.Severity == Severity.High);
    }

    [Fact] public void AnalyzeBackups_Stale_ProducesHigh()
    {
        Assert.Contains(Analyzers.AnalyzeBackups([new("SRV01", "Export", "C:\\Exports", 24, true, "a.csv", null, 48, null)]), x => x.Message.Contains("hours old"));
    }

    [Fact] public void AnalyzeBackups_PathMissing_ProducesHigh()
    {
        Assert.Contains(Analyzers.AnalyzeBackups([new("SRV01", "Export", "C:\\Exports", 24, false, null, null, null, null)]), x => x.Message.Contains("missing"));
    }

    [Fact] public void AnalyzeCertificates_Expired_ProducesCritical()
    {
        Assert.Contains(Analyzers.AnalyzeCertificates([new("SRV01", "CN=a", "CA", "abc", "2025", "2026", -1, "My")]), x => x.Severity == Severity.Critical);
    }

    [Fact] public void AnalyzeCertificates_ExpiringSoon_ProducesHigh()
    {
        Assert.Contains(Analyzers.AnalyzeCertificates([new("SRV01", "CN=a", "CA", "abc", "2025", "2026", 20, "My")]), x => x.Severity == Severity.High);
    }
}
