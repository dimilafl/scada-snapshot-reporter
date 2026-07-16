using OtSnapshotReporter.Infrastructure;
using OtSnapshotReporter.Models;

namespace OtSnapshotReporter.Analysis;

public static class FindingPostProcessors
{
    public static List<Finding> AddCorrelationFindings(IEnumerable<Finding> findings, int minimumServers = 3)
    {
        var all = findings.ToList();
        var correlated = all
            .Where(x => !string.IsNullOrWhiteSpace(x.Server) && x.Server != "(multiple)")
            .GroupBy(x => Helpers.Key(x.Module, x.Subject, NormalizeMessage(x.Message)), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Server).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= minimumServers)
            .Select(g =>
            {
                var first = g.First();
                var servers = g.Select(x => x.Server).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
                var severity = g.Max(x => x.Severity);
                return Finding.Create(
                    "correlation",
                    "(multiple)",
                    first.Subject,
                    severity,
                    $"{servers.Count} servers share the same {first.Module} finding: {first.Message}. A common dependency or upstream outage is likely. Servers: {string.Join(", ", servers)}");
            });

        all.AddRange(correlated);
        return all;
    }

    public static List<Finding> ApplyMaintenanceWindows(IEnumerable<Finding> findings, MaintenanceWindowsConfig config, DateTime timestamp)
    {
        if (config.Windows.Count == 0)
        {
            return findings.ToList();
        }

        return findings
            .Select(finding =>
            {
                var match = config.Windows.FirstOrDefault(window => Applies(window, finding, timestamp));
                if (match is null)
                {
                    return finding;
                }

                var reason = string.IsNullOrWhiteSpace(match.Reason) ? match.Name : match.Reason;
                return finding with
                {
                    Severity = Severity.Info,
                    Message = $"Suppressed by maintenance window '{match.Name}' ({reason}): {finding.Message}"
                };
            })
            .ToList();
    }

    private static bool Applies(MaintenanceWindow window, Finding finding, DateTime timestamp)
    {
        if (!Helpers.TryParseTimestamp(window.Start, out var start) || !Helpers.TryParseTimestamp(window.End, out var end))
        {
            return false;
        }

        if (timestamp < start || timestamp > end)
        {
            return false;
        }

        if (window.Servers is { Count: > 0 } &&
            !window.Servers.Contains("*", StringComparer.OrdinalIgnoreCase) &&
            !window.Servers.Contains(finding.Server, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        if (window.Modules is { Count: > 0 } &&
            !window.Modules.Contains("*", StringComparer.OrdinalIgnoreCase) &&
            !window.Modules.Contains(finding.Module, StringComparer.OrdinalIgnoreCase))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeMessage(string message) =>
        message
            .Replace("Critical", "", StringComparison.OrdinalIgnoreCase)
            .Replace("High", "", StringComparison.OrdinalIgnoreCase)
            .Trim();
}
