using OtSnapshotReporter.Models;

namespace OtSnapshotReporter.Infrastructure;

public static class Helpers
{
    public static string Key(params string?[] parts) => string.Join("|", parts.Select(x => x ?? ""));
    public static bool EqualsText(string? left, string? right) => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
    public static Severity ParseSeverity(string? value, Severity fallback) => Enum.TryParse<Severity>(value, true, out var parsed) ? parsed : fallback;
}
