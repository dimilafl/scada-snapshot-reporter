using OtSnapshotReporter.Models;

using System.Globalization;

namespace OtSnapshotReporter.Infrastructure;

public static class Helpers
{
    public static string Key(params string?[] parts) => string.Join("|", parts.Select(x => x?.Trim() ?? ""));
    public static bool EqualsText(string? left, string? right) => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
    public static Severity ParseSeverity(string? value, Severity fallback) => Enum.TryParse<Severity>(value, true, out var parsed) ? parsed : fallback;

    public static bool TryParseTimestamp(string? value, out DateTime result)
    {
        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var invariant))
        {
            result = invariant.LocalDateTime;
            return true;
        }

        if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out result))
        {
            return true;
        }

        result = default;
        return false;
    }
}
