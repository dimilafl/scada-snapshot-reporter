using System.Text.Json;

namespace OtSnapshotReporter.Tests;

public sealed class LoadingTests
{
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
    public void ResolveRawRoot_EmptyDirectory_ReturnsDefaultRawPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(dir);
        var result = Loading.ResolveRawRoot(dir, Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()));
        Directory.Delete(dir);
        Assert.Equal(Path.Combine(dir, "raw"), result);
    }
}
