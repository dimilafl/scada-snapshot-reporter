using System.Text.Json;
using System.Text.Json.Serialization;

namespace OtSnapshotGui;

internal sealed class GuiSettings
{
    [JsonPropertyName("config_path")] public string ConfigPath { get; set; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "config"));
    [JsonPropertyName("output_root")] public string OutputRoot { get; set; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Output"));
    [JsonPropertyName("collector_script")] public string CollectorScript { get; set; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "collectors", "Run-Collectors.ps1"));
    [JsonPropertyName("engine_exe")] public string EngineExe { get; set; } = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "OtSnapshotReporter", "bin", "Release", "net8.0", "win-x64", "publish", "OtSnapshotReporter.exe"));
    [JsonPropertyName("last_report_path")] public string LastReportPath { get; set; } = "";

    public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "gui-settings.json");

    public static GuiSettings Load()
    {
        if (!File.Exists(SettingsPath))
        {
            return new GuiSettings();
        }

        return JsonSerializer.Deserialize<GuiSettings>(File.ReadAllText(SettingsPath), JsonOptions()) ?? new GuiSettings();
    }

    public void Save() => File.WriteAllText(SettingsPath, JsonSerializer.Serialize(this, JsonOptions()));

    private static JsonSerializerOptions JsonOptions() => new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
}
