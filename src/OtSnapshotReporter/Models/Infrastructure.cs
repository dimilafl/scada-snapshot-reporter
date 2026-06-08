namespace OtSnapshotReporter.Models;

public enum Severity { Info, Low, Medium, High, Critical }

public sealed record Finding(string Module, string Server, string Subject, Severity Severity, string Message)
{
    public static Finding Create(string module, string? server, string? subject, Severity severity, string message) =>
        new(module, server ?? "", subject ?? "", severity, message);
}

public sealed record PreviousSnapshot(
    IReadOnlyCollection<ServiceRecord> Services,
    IReadOnlyCollection<DiskRecord> Disks,
    IReadOnlyCollection<TaskRecord> Tasks,
    IReadOnlyCollection<UptimeRecord> Uptimes,
    IReadOnlyCollection<SoftwareRecord> Software,
    IReadOnlyCollection<DriverRecord> Drivers,
    IReadOnlyCollection<EventLogSummaryRecord> EventLogs,
    IReadOnlyCollection<FileShareRecord> FileShares,
    IReadOnlyCollection<BackupFreshnessRecord> Backups)
{
    public static PreviousSnapshot Empty { get; } = new([], [], [], [], [], [], [], [], []);
}

public sealed record ModuleDescriptor(string ModuleKey, string DisplayName, string HtmlFile, string RawJsonFile, string? ExpectedConfigFile, bool HasMatrixLayout);

public sealed record AppOptions(string InputPath, string ConfigPath, string OutputPath, string? PreviousPath, bool AcceptBaseline)
{
    public static AppOptions Parse(string[] args)
    {
        var input = ".";
        var config = ".\\config";
        var output = ".\\Output";
        string? previous = null;
        var acceptBaseline = false;

        for (var i = 0; i < args.Length; i++)
        {
            var value = i + 1 < args.Length ? args[i + 1] : null;
            switch (args[i])
            {
                case "--input" when value is not null:
                    input = value;
                    i++;
                    break;
                case "--config" when value is not null:
                    config = value;
                    i++;
                    break;
                case "--output" when value is not null:
                    output = value;
                    i++;
                    break;
                case "--previous" when value is not null:
                    previous = value;
                    i++;
                    break;
                case "--accept-baseline":
                    acceptBaseline = true;
                    break;
            }
        }

        return new AppOptions(input, config, output, previous, acceptBaseline);
    }
}
