using System.Text.Json;

namespace OtSnapshotReporter.Tests;

public sealed class InfrastructureTests
{
    [Fact] public void Key_TwoArguments_ReturnsJoined() => Assert.Equal("a|b", Helpers.Key("a", "b"));
    [Fact] public void Key_ThreeArguments_ReturnsJoined() => Assert.Equal("a|b|c", Helpers.Key("a", "b", "c"));
    [Fact] public void EqualsText_NullVsNull_ReturnsTrue() => Assert.True(Helpers.EqualsText(null, null));
    [Fact] public void EqualsText_NullVsEmpty_ReturnsTrue() => Assert.True(Helpers.EqualsText(null, ""));
    [Fact] public void EqualsText_SameString_ReturnsTrue() => Assert.True(Helpers.EqualsText("ABC", "abc"));
    [Fact] public void EqualsText_DifferentString_ReturnsFalse() => Assert.False(Helpers.EqualsText("ABC", "def"));
    [Fact] public void ParseSeverity_Critical_ReturnsCritical() => Assert.Equal(Severity.Critical, Helpers.ParseSeverity("Critical", Severity.Info));
    [Fact] public void ParseSeverity_Unknown_ReturnsInfo() => Assert.Equal(Severity.Info, Helpers.ParseSeverity("nope", Severity.Info));

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
}
