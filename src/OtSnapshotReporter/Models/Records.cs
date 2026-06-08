namespace OtSnapshotReporter.Models;

public sealed record ServiceRecord(string Server, string Name, string DisplayName, string Status, string StartupType, string StartName);
public sealed record DiskRecord(string Server, string Drive, double TotalGb, double FreeGb, double FreePercent);
public sealed record TaskRecord(string Server, string TaskPath, string TaskName, bool Enabled, string? State, string? LastRunTime, long? LastTaskResult, string? NextRunTime, string? RunAs, string? Action);
public sealed record UptimeRecord(string Server, string LastBootTime, double UptimeHours);
public sealed record SoftwareRecord(string Server, string Name, string? Version, string? Publisher, string? InstallLocation);
public sealed record DriverRecord(string Server, string Type, string Name, string? Version, string Architecture, string? InstallPath, string? LastModified);
public sealed record ErrorRecord(string Server, string Error);
public sealed record EventLogSummaryRecord(string Server, string LogName, string Source, int Level, int Count, string? NewestTime, int? NewestEventId, int WindowHours);
public sealed record FileShareRecord(string Server, string? Name, string Path, bool Reachable, string? Error, string? CheckedAt);
public sealed record BackupFreshnessRecord(string Server, string? Name, string Path, double? MaxAgeHours, bool Exists, string? NewestFile, string? NewestWriteTime, double? AgeHours, string? Error);
public sealed record OdbcDsnRecord(string Server, string DsnName, string DriverName, string Type, string Architecture, string? ServerTarget, string? Database, bool ConnectionPassed);
public sealed record CertificateRecord(string Server, string Subject, string Issuer, string Thumbprint, string NotBefore, string NotAfter, int DaysUntilExpiry, string Store);
public sealed record SqlAgentJobRecord(string Server, string Instance, string JobName, bool Enabled, string? LastRunDate, string? LastRunTime, int? LastRunStatus, int? LastRunDuration, string? LastRunMessage, string? DateCreated, string? DateModified, string? JobOwner);
public sealed record SsrsSubscriptionRecord(string Server, string Instance, string ReportPath, string SubscriptionDescription, string? Owner, bool OwnerExists, string? LastStatus, string? LastRunTime, bool Enabled);
