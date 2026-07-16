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
}
