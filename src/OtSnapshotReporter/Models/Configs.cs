using System.Text.Json.Serialization;

namespace OtSnapshotReporter.Models;

public sealed class Thresholds
{
    [JsonPropertyName("disk_free_percent_warning")] public double DiskFreePercentWarning { get; set; } = 15;
    [JsonPropertyName("disk_free_percent_critical")] public double DiskFreePercentCritical { get; set; } = 10;
    [JsonPropertyName("task_not_run_hours_warning")] public double TaskNotRunHoursWarning { get; set; } = 24;
    [JsonPropertyName("reboot_detection_enabled")] public bool RebootDetectionEnabled { get; set; } = true;
    [JsonPropertyName("disk_drop_percent_warning")] public double DiskDropPercentWarning { get; set; } = 10;
    [JsonPropertyName("snapshot_retention_days")] public int SnapshotRetentionDays { get; set; } = 90;
}

public sealed record ExpectedServicesConfig(List<ExpectedService> Services)
{
    public ExpectedServicesConfig() : this([]) { }
}
public sealed record ExpectedService(string Server, string Name, [property: JsonPropertyName("expected_status")] string ExpectedStatus, [property: JsonPropertyName("expected_startup_type")] string? ExpectedStartupType, [property: JsonPropertyName("severity_if_stopped")] string? SeverityIfStopped);
public sealed record ExpectedTasksConfig(List<ExpectedTask> Tasks)
{
    public ExpectedTasksConfig() : this([]) { }
}
public sealed record ExpectedTask(string Server, [property: JsonPropertyName("task_path")] string TaskPath, [property: JsonPropertyName("task_name")] string TaskName, [property: JsonPropertyName("expected_enabled")] bool? ExpectedEnabled);
public sealed record ExpectedSoftwareConfig(List<ExpectedSoftware> Software)
{
    public ExpectedSoftwareConfig() : this([]) { }
}
public sealed record ExpectedSoftware(string Server, string Name, [property: JsonPropertyName("expected_version")] string ExpectedVersion);
public sealed record ExpectedDriversConfig(List<ExpectedDriver> Drivers)
{
    public ExpectedDriversConfig() : this([]) { }
}
public sealed record ExpectedDriver(string Server, string Type, string Name, string Architecture, [property: JsonPropertyName("expected_version")] string ExpectedVersion);
public sealed record SharesConfig(List<ShareEntry> Shares)
{
    public SharesConfig() : this([]) { }
}
public sealed record ShareEntry(string Name, string Path);
public sealed record ExpectedPathsConfig(List<ExpectedPath> Paths)
{
    public ExpectedPathsConfig() : this([]) { }
}
public sealed record ExpectedPath(string Name, string Path, [property: JsonPropertyName("max_age_hours")] double MaxAgeHours);

public sealed record MaintenanceWindowsConfig(List<MaintenanceWindow> Windows)
{
    public MaintenanceWindowsConfig() : this([]) { }
}
public sealed record MaintenanceWindow(
    string Name,
    string Start,
    string End,
    List<string>? Servers,
    List<string>? Modules,
    string? Reason);
