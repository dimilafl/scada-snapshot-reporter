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

    [Fact] public void Diff_MultipleChanges_ProducesMultipleFindings()
    {
        var current = new TaskRecord("SRV01", "\\", "A", false, null, null, null, null, "B", "new");
        var previous = new TaskRecord("SRV01", "\\", "A", true, null, null, null, null, "A", "old");
        Assert.Equal(3, DiffEngine.DiffTasks([current], [previous]).Count());
    }
}
