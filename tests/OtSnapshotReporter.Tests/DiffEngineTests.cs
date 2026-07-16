namespace OtSnapshotReporter.Tests;

public sealed class DiffEngineTests
{
    [Fact] public void Diff_NoPrevious_ProducesNoFindings()
    {
        Assert.Empty(DiffEngine.DiffSoftware([new("SRV01", "A", "1", null, null)], []));
    }

    [Fact] public void Diff_ItemPresentInBoth_NoChange_ProducesNoFindings()
    {
        var item = new SoftwareRecord("SRV01", "A", "1", null, null);
        Assert.Empty(DiffEngine.DiffSoftware([item], [item]));
    }

    [Fact] public void Diff_ItemInPrevious_NotInCurrent_ProducesDisappeared()
    {
        Assert.Contains(DiffEngine.DiffSoftware([], [new("SRV01", "A", "1", null, null)]), x => x.Message.Contains("disappeared"));
    }

    [Fact] public void Diff_ItemInCurrent_NotInPrevious_ProducesNew()
    {
        Assert.Contains(DiffEngine.DiffSoftware([new("SRV01", "A", "1", null, null)], [new("SRV01", "B", "1", null, null)]), x => x.Message.Contains("New software"));
    }

    [Fact] public void Diff_FieldChanged_ProducesFindingWithCorrectSeverity()
    {
        var findings = DiffEngine.DiffSoftware([new("SRV01", "A", "2", null, null)], [new("SRV01", "A", "1", null, null)]).ToList();
        Assert.Contains(findings, x => x.Severity == Severity.Medium && x.Message.Contains("Version changed"));
    }

    [Fact]
    public void Diff_WhitespaceAroundIdentity_DoesNotCreateFalseDrift()
    {
        var current = new SoftwareRecord(" SRV01 ", " A ", "1", null, null);
        var previous = new SoftwareRecord("SRV01", "A", "1", null, null);

        Assert.Empty(DiffEngine.DiffSoftware([current], [previous]));
    }

    [Fact] public void Diff_MultipleChanges_ProducesMultipleFindings()
    {
        var current = new TaskRecord("SRV01", "\\", "A", false, null, null, null, null, "B", "new");
        var previous = new TaskRecord("SRV01", "\\", "A", true, null, null, null, null, "A", "old");
        Assert.Equal(3, DiffEngine.DiffTasks([current], [previous]).Count());
    }

    [Fact]
    public void Diff_CaseOnlyDuplicateIdentity_UsesOneRecord()
    {
        var current = new[]
        {
            new ServiceRecord("SRV01", "Demo", "Demo", "Running", "Automatic", "SYSTEM"),
            new ServiceRecord("srv01", "demo", "Demo", "Running", "Automatic", "SYSTEM")
        };
        var previous = new[]
        {
            new ServiceRecord("srv01", "DEMO", "Demo", "Running", "Automatic", "SYSTEM")
        };

        var exception = Record.Exception(() => DiffEngine.DiffServices(current, previous).ToList());

        Assert.Null(exception);
    }

    [Fact]
    public void DiffFileShares_DisappearedShare_DoesNotEmitNullFinding()
    {
        var previous = new FileShareRecord("SRV01", "Archive", "\\\\server\\archive", true, null, null);

        var findings = DiffEngine.DiffFileShares([], [previous]).ToList();

        Assert.Empty(findings);
    }

    [Fact]
    public void DiffBackups_DisappearedPath_DoesNotEmitNullFinding()
    {
        var previous = new BackupFreshnessRecord("SRV01", "Exports", "C:\\Exports", 24, true, "latest.csv", null, 48, null);

        var findings = DiffEngine.DiffBackups([], [previous]).ToList();

        Assert.Empty(findings);
    }

    [Fact]
    public void DiffOdbcDsns_ConnectionFailure_ProducesHighFinding()
    {
        var current = new OdbcDsnRecord("SRV01", "Historian", "New Driver", "System", "64-bit", null, null, false);
        var previous = new OdbcDsnRecord("SRV01", "Historian", "New Driver", "System", "64-bit", null, null, true);

        var finding = Assert.Single(DiffEngine.DiffOdbcDsns([current], [previous]));

        Assert.Equal(Severity.High, finding.Severity);
        Assert.Contains("passed to failed", finding.Message);
    }

    [Fact]
    public void DiffOdbcDsns_NewDsn_ProducesMediumFinding()
    {
        var existing = new OdbcDsnRecord("SRV01", "Existing", "Driver", "System", "64-bit", null, null, true);
        var current = new OdbcDsnRecord("SRV01", "Historian", "Driver", "System", "64-bit", null, null, true);

        var finding = Assert.Single(DiffEngine.DiffOdbcDsns([existing, current], [existing]));

        Assert.Equal(Severity.Medium, finding.Severity);
        Assert.Contains("New ODBC DSN", finding.Message);
    }

    [Fact]
    public void DiffCertificates_ExpirationChange_ProducesMediumFinding()
    {
        var current = new CertificateRecord("SRV01", "CN=Demo", "Demo CA", "abc", "2026-01-01", "2027-01-01", 100, "My");
        var previous = current with { NotAfter = "2026-12-01" };

        var finding = Assert.Single(DiffEngine.DiffCertificates([current], [previous]));

        Assert.Equal(Severity.Medium, finding.Severity);
        Assert.Contains("expiration changed", finding.Message);
    }

    [Fact]
    public void DiffCertificates_SameCertificateInDifferentStore_IsNotCollapsed()
    {
        var current = new CertificateRecord("SRV01", "CN=Demo", "Demo CA", "abc", "2026-01-01", "2027-01-01", 100, "My");
        var previous = current with { Store = "Root" };

        var findings = DiffEngine.DiffCertificates([current], [previous]).ToList();

        Assert.Contains(findings, x => x.Message.Contains("disappeared"));
        Assert.Contains(findings, x => x.Message.Contains("New certificate"));
    }

    [Fact]
    public void DiffCertificates_NewCertificate_ProducesLowFinding()
    {
        var existing = new CertificateRecord("SRV01", "CN=Existing", "Demo CA", "existing", "2026-01-01", "2027-01-01", 100, "My");
        var current = new CertificateRecord("SRV01", "CN=New", "Demo CA", "new", "2026-01-01", "2027-01-01", 100, "My");

        var finding = Assert.Single(DiffEngine.DiffCertificates([existing, current], [existing]));

        Assert.Equal(Severity.Low, finding.Severity);
        Assert.Contains("New certificate", finding.Message);
    }

    [Fact]
    public void DiffSqlAgentJobs_StatusChange_ProducesFinding()
    {
        var current = new SqlAgentJobRecord("SRV01", ".", "Nightly", true, "20260716", "010000", 0, 4, "Failed", null, null, "job-owner-new");
        var previous = current with { LastRunStatus = 1, JobOwner = "job-owner-old" };

        var findings = DiffEngine.DiffSqlAgentJobs([current], [previous]).ToList();

        Assert.Contains(findings, x => x.Severity == Severity.High && x.Message.Contains("last run status changed"));
        Assert.Contains(findings, x => x.Severity == Severity.Low && x.Message.Contains("owner changed"));
    }

    [Fact]
    public void DiffSqlAgentJobs_NewJob_ProducesMediumFinding()
    {
        var existing = new SqlAgentJobRecord("SRV01", ".", "Existing", true, null, null, 1, null, null, null, null, "owner");
        var current = new SqlAgentJobRecord("SRV01", ".", "Nightly", true, null, null, 1, null, null, null, null, "owner");

        var finding = Assert.Single(DiffEngine.DiffSqlAgentJobs([existing, current], [existing]));

        Assert.Equal(Severity.Medium, finding.Severity);
        Assert.Contains("New SQL Agent job", finding.Message);
    }

    [Fact]
    public void DiffSsrsSubscriptions_OwnerLoss_ProducesHighFinding()
    {
        var current = new SsrsSubscriptionRecord("SRV01", ".", "/Reports/Daily", "Daily", "disabled-user", false, "Done", null, true);
        var previous = current with { Owner = "operator", OwnerExists = true };

        var finding = Assert.Single(DiffEngine.DiffSsrsSubscriptions([current], [previous]));

        Assert.Equal(Severity.High, finding.Severity);
        Assert.Contains("owner availability changed", finding.Message);
    }

    [Fact]
    public void DiffSsrsSubscriptions_NewSubscription_ProducesMediumFinding()
    {
        var existing = new SsrsSubscriptionRecord("SRV01", ".", "/Reports/Existing", "Existing", "operator", true, "Done", null, true);
        var current = new SsrsSubscriptionRecord("SRV01", ".", "/Reports/Daily", "Daily", "operator", true, "Done", null, true);

        var finding = Assert.Single(DiffEngine.DiffSsrsSubscriptions([existing, current], [existing]));

        Assert.Equal(Severity.Medium, finding.Severity);
        Assert.Contains("New SSRS subscription", finding.Message);
    }
}
