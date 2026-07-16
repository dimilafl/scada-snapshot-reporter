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

public sealed record AppOptions(string InputPath, string ConfigPath, string OutputPath, string? PreviousPath, bool AcceptBaseline, bool HelpRequested)
{
    public static string Usage => """
        OT Snapshot Reporter

        Usage:
          dotnet run --project .\src\OtSnapshotReporter -- [options]

        Options:
          --input <path>       Snapshot input folder, raw folder, or per-server collection root.
          --config <path>      Configuration folder. Default: .\config
          --output <path>      Report output folder. Default: .\Output
          --previous <path>    Previous snapshot folder for drift comparison.
          --accept-baseline    Rewrite expected service/task/software/driver baselines.
          --help, -h           Show this help text.
        """;

    public static AppOptions Parse(string[] args)
    {
        var input = ".";
        var config = ".\\config";
        var output = ".\\Output";
        string? previous = null;
        var acceptBaseline = false;
        var helpRequested = false;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--input":
                    input = ReadValue(args, ref i, "--input");
                    break;
                case "--config":
                    config = ReadValue(args, ref i, "--config");
                    break;
                case "--output":
                    output = ReadValue(args, ref i, "--output");
                    break;
                case "--previous":
                    previous = ReadValue(args, ref i, "--previous");
                    break;
                case "--accept-baseline":
                    acceptBaseline = true;
                    break;
                case "--help":
                case "-h":
                    helpRequested = true;
                    break;
                default:
                    throw new ArgumentException($"Unknown option '{args[i]}'. Use --help to see supported options.");
            }
        }

        return new AppOptions(input, config, output, previous, acceptBaseline, helpRequested);
    }

    private static string ReadValue(string[] args, ref int index, string option)
    {
        if (index + 1 >= args.Length || args[index + 1].StartsWith("-", StringComparison.Ordinal))
        {
            throw new ArgumentException($"{option} requires a value.");
        }

        index++;
        return args[index];
    }
}
