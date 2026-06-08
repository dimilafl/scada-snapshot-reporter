namespace OtSnapshotReporter.Tests;

public sealed class Phase3ModuleTests
{
    [Fact]
    public void AnalyzeSqlAgentJobs_FailedEnabledJob_ProducesHighFinding()
    {
        var job = new SqlAgentJobRecord("SRV01", ".", "Nightly ETL", true, DateTime.UtcNow.ToString("yyyyMMdd"), "020000", 0, 12, "Failed", null, null, "svc");
        var finding = Assert.Single(Analyzers.AnalyzeSqlAgentJobs([job]));
        Assert.Equal("sql_agent_jobs", finding.Module);
        Assert.Equal(Severity.High, finding.Severity);
    }

    [Fact]
    public void AnalyzeSqlAgentJobs_DisabledJob_ProducesNoFindings()
    {
        var job = new SqlAgentJobRecord("SRV01", ".", "Old Job", false, null, null, 0, null, "Failed", null, null, "svc");
        Assert.Empty(Analyzers.AnalyzeSqlAgentJobs([job]));
    }

    [Fact]
    public void AnalyzeSsrsSubscriptions_MissingOwner_ProducesHighFinding()
    {
        var subscription = new SsrsSubscriptionRecord("SRV01", ".", "/SCADA/Daily", "Daily", "OLD\\user", false, "Done", null, true);
        var finding = Assert.Single(Analyzers.AnalyzeSsrsSubscriptions([subscription]));
        Assert.Equal("ssrs_subscriptions", finding.Module);
        Assert.Equal(Severity.High, finding.Severity);
    }

    [Fact]
    public void Correlation_ThreeServersSameFinding_AddsCorrelationFinding()
    {
        var findings = new[]
        {
            Finding.Create("file_shares", "A", "Archive", Severity.High, "Share is unreachable"),
            Finding.Create("file_shares", "B", "Archive", Severity.High, "Share is unreachable"),
            Finding.Create("file_shares", "C", "Archive", Severity.High, "Share is unreachable")
        };

        var result = FindingPostProcessors.AddCorrelationFindings(findings);
        Assert.Contains(result, x => x.Module == "correlation" && x.Message.Contains("3 servers"));
    }

    [Fact]
    public void MaintenanceWindow_MatchingFinding_DowngradesToInfo()
    {
        var now = DateTime.Now;
        var config = new MaintenanceWindowsConfig([
            new("Patch", now.AddMinutes(-1).ToString("o"), now.AddMinutes(30).ToString("o"), ["SRV01"], ["services"], "monthly patching")
        ]);
        var finding = Finding.Create("services", "SRV01", "EventLog", Severity.High, "Service status is Stopped");

        var result = FindingPostProcessors.ApplyMaintenanceWindows([finding], config, now);

        var suppressed = Assert.Single(result);
        Assert.Equal(Severity.Info, suppressed.Severity);
        Assert.Contains("Suppressed by maintenance window", suppressed.Message);
    }
}
