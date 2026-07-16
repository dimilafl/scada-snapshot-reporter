using System.Net;
using System.Text;
using OtSnapshotReporter.Infrastructure;
using OtSnapshotReporter.Models;

namespace OtSnapshotReporter.Reporting;

public static class HtmlReport
{
    public static void WriteIndex(string path, IReadOnlyCollection<Finding> findings, IEnumerable<(string href, string label)> pages)
    {
        var counts = Enum.GetValues<Severity>().Reverse().Select(severity => (severity, count: findings.Count(x => x.Severity == severity)));
        var rows = string.Join(Environment.NewLine, counts.Select(x => $"<tr><td>{x.severity}</td><td>{x.count}</td></tr>"));
        var findingRows = string.Join(Environment.NewLine, findings.OrderByDescending(x => x.Severity).Select(FindingRow));
        var nav = string.Join(Environment.NewLine, pages.Select(x => $"<a href=\"{Encode(x.href)}\">{Encode(x.label)}</a>"));
        var serverRows = string.Join(Environment.NewLine, findings
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Server) ? "(unknown)" : x.Server, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .Select(x => $"<tr><td>{Encode(x.Key)}</td><td>{x.Count(f => f.Severity == Severity.Critical)}</td><td>{x.Count(f => f.Severity == Severity.High)}</td><td>{x.Count(f => f.Severity == Severity.Medium)}</td><td>{x.Count(f => f.Severity == Severity.Low)}</td><td>{x.Count(f => f.Severity == Severity.Info)}</td><td>{x.Count()}</td></tr>"));
        var changes = findings
            .Where(x => IsChangeFinding(x.Message))
            .OrderByDescending(x => x.Severity)
            .ToList();
        var changeRows = string.Join(Environment.NewLine, changes.Select(FindingRow));
        var noIssues = findings.Count == 0 ? "<section class=\"no-issues\">No issues found in this snapshot.</section>" : "";

        WritePage(path, "OT Snapshot Summary", $"""
            <nav>{nav}</nav>
            {noIssues}
            <h2>Severity Counts</h2>
            <table><thead><tr><th>Severity</th><th>Count</th></tr></thead><tbody>{rows}</tbody></table>
            <h2>Per-Server Summary</h2>
            <table><thead><tr><th>Server</th><th>Critical</th><th>High</th><th>Medium</th><th>Low</th><th>Info</th><th>Total</th></tr></thead><tbody>{serverRows}</tbody></table>
            <h2>Changes Since Last Snapshot</h2>
            <table><thead><tr><th>Module</th><th>Server</th><th>Subject</th><th>Severity</th><th>Message</th></tr></thead><tbody>{changeRows}</tbody></table>
            <h2>Findings</h2>
            <table><thead><tr><th>Module</th><th>Server</th><th>Subject</th><th>Severity</th><th>Message</th></tr></thead><tbody>{findingRows}</tbody></table>
            """);
    }

    public static void WriteTable<T>(string path, string title, IEnumerable<T> rows, IEnumerable<Finding> findings, string module)
    {
        var properties = typeof(T).GetProperties();
        var header = string.Join("", properties.Select(x => $"<th>{Encode(x.Name)}</th>"));
        var body = string.Join(Environment.NewLine, rows.Select(row => "<tr>" + string.Join("", properties.Select(p => $"<td>{Encode(p.GetValue(row)?.ToString() ?? "")}</td>")) + "</tr>"));
        var moduleFindings = string.Join(Environment.NewLine, findings.Where(x => x.Module == module).OrderByDescending(x => x.Severity).Select(FindingRow));

        WritePage(path, title, $"""
            <p><a href="index.html">Back to summary</a></p>
            <h2>Findings</h2>
            <table><thead><tr><th>Module</th><th>Server</th><th>Subject</th><th>Severity</th><th>Message</th></tr></thead><tbody>{moduleFindings}</tbody></table>
            <h2>Raw Records</h2>
            <table><thead><tr>{header}</tr></thead><tbody>{body}</tbody></table>
            """);
    }

    public static void WriteFindingsPage(string path, string title, IEnumerable<Finding> findings, string module)
    {
        var moduleFindings = string.Join(Environment.NewLine, findings.Where(x => x.Module == module).OrderByDescending(x => x.Severity).Select(FindingRow));
        WritePage(path, title, $"""
            <p><a href="index.html">Back to summary</a></p>
            <h2>Findings</h2>
            <table><thead><tr><th>Module</th><th>Server</th><th>Subject</th><th>Severity</th><th>Message</th></tr></thead><tbody>{moduleFindings}</tbody></table>
            """);
    }

    public static void WriteSoftwareMatrix(string path, IEnumerable<SoftwareRecord> records, ExpectedSoftwareConfig expected, IEnumerable<Finding> findings)
    {
        var software = records.ToList();
        var servers = software.Select(x => x.Server).Concat(expected.Software.Select(x => x.Server)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var names = software.Select(x => x.Name).Concat(expected.Software.Select(x => x.Name)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var expectedByName = expected.Software.GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => string.Join("; ", x.Select(e => $"{e.Server}: {e.ExpectedVersion}")), StringComparer.OrdinalIgnoreCase);
        var header = "<th>Component</th>" + string.Join("", servers.Select(x => $"<th>{Encode(x)}</th>")) + "<th>Expected</th>";
        var rows = string.Join(Environment.NewLine, names.Select(name =>
        {
            var serverCells = string.Join("", servers.Select(server =>
            {
                var version = software.FirstOrDefault(x => SameText(x.Server, server) && SameText(x.Name, name))?.Version ?? "";
                return $"<td>{Encode(version)}</td>";
            }));
            expectedByName.TryGetValue(name, out var expectedVersion);
            return $"<tr><td>{Encode(name)}</td>{serverCells}<td>{Encode(expectedVersion ?? "")}</td></tr>";
        }));
        var moduleFindings = string.Join(Environment.NewLine, findings.Where(x => x.Module == "software").OrderByDescending(x => x.Severity).Select(FindingRow));
        WritePage(path, "Software Version Matrix", $"""
            <p><a href="index.html">Back to summary</a></p>
            <h2>Findings</h2>
            <table><thead><tr><th>Module</th><th>Server</th><th>Subject</th><th>Severity</th><th>Message</th></tr></thead><tbody>{moduleFindings}</tbody></table>
            <h2>Matrix</h2>
            <table><thead><tr>{header}</tr></thead><tbody>{rows}</tbody></table>
            """);
    }

    public static void WriteDriverMatrix(string path, IEnumerable<DriverRecord> records, ExpectedDriversConfig expected, IEnumerable<Finding> findings)
    {
        var drivers = records.ToList();
        var servers = drivers.Select(x => x.Server).Concat(expected.Drivers.Select(x => x.Server)).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var components = drivers.Select(x => DriverComponent(x.Type, x.Name, x.Architecture)).Concat(expected.Drivers.Select(x => DriverComponent(x.Type, x.Name, x.Architecture))).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        var expectedByComponent = expected.Drivers.GroupBy(x => DriverComponent(x.Type, x.Name, x.Architecture), StringComparer.OrdinalIgnoreCase).ToDictionary(x => x.Key, x => string.Join("; ", x.Select(e => $"{e.Server}: {e.ExpectedVersion}")), StringComparer.OrdinalIgnoreCase);
        var header = "<th>Component</th>" + string.Join("", servers.Select(x => $"<th>{Encode(x)}</th>")) + "<th>Expected</th>";
        var rows = string.Join(Environment.NewLine, components.Select(component =>
        {
            var serverCells = string.Join("", servers.Select(server =>
            {
                var version = drivers.FirstOrDefault(x => SameText(x.Server, server) && SameText(DriverComponent(x.Type, x.Name, x.Architecture), component))?.Version ?? "";
                return $"<td>{Encode(version)}</td>";
            }));
            expectedByComponent.TryGetValue(component, out var expectedVersion);
            return $"<tr><td>{Encode(component)}</td>{serverCells}<td>{Encode(expectedVersion ?? "")}</td></tr>";
        }));
        var moduleFindings = string.Join(Environment.NewLine, findings.Where(x => x.Module == "drivers").OrderByDescending(x => x.Severity).Select(FindingRow));
        WritePage(path, "ODBC/OLE DB Matrix", $"""
            <p><a href="index.html">Back to summary</a></p>
            <h2>Findings</h2>
            <table><thead><tr><th>Module</th><th>Server</th><th>Subject</th><th>Severity</th><th>Message</th></tr></thead><tbody>{moduleFindings}</tbody></table>
            <h2>Matrix</h2>
            <table><thead><tr>{header}</tr></thead><tbody>{rows}</tbody></table>
            """);
    }

    private static bool IsChangeFinding(string message) =>
        message.Contains("since previous snapshot", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("changed from", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("Version changed", StringComparison.OrdinalIgnoreCase) ||
        message.Contains("detected since previous", StringComparison.OrdinalIgnoreCase);

    private static string FindingRow(Finding x) =>
        $"<tr class=\"sev-{x.Severity.ToString().ToLowerInvariant()}\"><td>{Encode(x.Module)}</td><td>{Encode(x.Server)}</td><td>{Encode(x.Subject)}</td><td>{Encode(x.Severity.ToString())}</td><td>{Encode(x.Message)}</td></tr>";

    private static void WritePage(string path, string title, string body)
    {
        Writing.WriteTextAtomically(path, $$"""
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8">
              <title>{{Encode(title)}}</title>
              <style>
                body { font-family: Segoe UI, Arial, sans-serif; margin: 24px; color: #1f2933; }
                h1, h2 { font-weight: 600; }
                nav { display: flex; flex-wrap: wrap; gap: 12px; margin: 16px 0 24px; }
                table { border-collapse: collapse; width: 100%; margin: 12px 0 28px; font-size: 14px; }
                th, td { border: 1px solid #d8dee6; padding: 8px 10px; text-align: left; vertical-align: top; }
                th { background: #eef2f6; }
                .no-issues { background: #e8f7ed; border: 1px solid #8bc99b; padding: 12px 14px; margin: 16px 0; font-weight: 600; }
                .sev-critical td { background: #fde2e2; }
                .sev-high td { background: #ffe8cc; }
                .sev-medium td { background: #fff4bf; }
                .sev-low td { background: #e9f5ff; }
              </style>
            </head>
            <body>
              <h1>{{Encode(title)}}</h1>
              {{body}}
            </body>
            </html>
            """, Encoding.UTF8);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value);
    private static bool SameText(string? left, string? right) => string.Equals(left ?? "", right ?? "", StringComparison.OrdinalIgnoreCase);
    private static string DriverComponent(string? type, string? name, string? architecture) => $"{type} | {name} | {architecture}";
}
