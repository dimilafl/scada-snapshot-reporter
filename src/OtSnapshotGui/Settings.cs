using System.Text.Json;
using System.Text.Json.Serialization;

namespace OtSnapshotGui;

internal sealed class GuiSettings
{
    [JsonPropertyName("config_path")] public string ConfigPath { get; set; } = GuiPathDefaults.ConfigPath;
    [JsonPropertyName("output_root")] public string OutputRoot { get; set; } = GuiPathDefaults.OutputRoot;
    [JsonPropertyName("collector_script")] public string CollectorScript { get; set; } = GuiPathDefaults.CollectorScript;
    [JsonPropertyName("engine_exe")] public string EngineExe { get; set; } = GuiPathDefaults.EnginePath;
    [JsonPropertyName("last_report_path")] public string LastReportPath { get; set; } = "";

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "gui-settings.json");

    public static GuiSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new GuiSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(SettingsPath), JsonOptions()) ?? new GuiSettings();
            if (string.IsNullOrWhiteSpace(settings.ConfigPath)) settings.ConfigPath = GuiPathDefaults.ConfigPath;
            if (string.IsNullOrWhiteSpace(settings.OutputRoot)) settings.OutputRoot = GuiPathDefaults.OutputRoot;
            if (string.IsNullOrWhiteSpace(settings.CollectorScript)) settings.CollectorScript = GuiPathDefaults.CollectorScript;
            if (string.IsNullOrWhiteSpace(settings.EngineExe)) settings.EngineExe = GuiPathDefaults.EnginePath;
            settings.LastReportPath ??= "";
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return new GuiSettings();
        }
    }

    public void Save()
    {
        var contents = JsonSerializer.Serialize(this, JsonOptions());
        using var document = JsonDocument.Parse(contents);
        AtomicFile.WriteText(SettingsPath, contents);
    }

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}

internal static class GuiPathDefaults
{
    public static string RepositoryRoot { get; } = FindRepositoryRoot();
    public static string ConfigPath => Path.Combine(RepositoryRoot, "config");
    public static string OutputRoot => Path.Combine(RepositoryRoot, "Output");
    public static string CollectorScript => Path.Combine(RepositoryRoot, "collectors", "Run-Collectors.ps1");
    public static string EnginePath => FindEnginePath();

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "collectors", "Run-Collectors.ps1")) &&
                File.Exists(Path.Combine(current.FullName, "config", "servers.json")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string FindEnginePath()
    {
        var candidates = new[]
        {
            Path.Combine(RepositoryRoot, "src", "OtSnapshotReporter", "bin", "Release", "net8.0", "win-x64", "publish", "OtSnapshotReporter.exe"),
            Path.Combine(RepositoryRoot, "src", "OtSnapshotReporter", "bin", "Release", "net8.0", "win-x64", "publish", "OtSnapshotReporter.dll"),
            Path.Combine(RepositoryRoot, "src", "OtSnapshotReporter", "bin", "Release", "net8.0", "OtSnapshotReporter.exe"),
            Path.Combine(RepositoryRoot, "src", "OtSnapshotReporter", "bin", "Release", "net8.0", "OtSnapshotReporter.dll"),
            Path.Combine(AppContext.BaseDirectory, "OtSnapshotReporter.exe"),
            Path.Combine(AppContext.BaseDirectory, "OtSnapshotReporter.dll")
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[0];
    }
}
