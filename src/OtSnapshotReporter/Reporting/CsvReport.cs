using System.Text;
using OtSnapshotReporter.Models;

namespace OtSnapshotReporter.Reporting;

public static class CsvReport
{
    public static void WriteFindings(string path, IEnumerable<Finding> findings)
    {
        var lines = new List<string> { "Module,Server,Subject,Severity,Message" };
        lines.AddRange(findings.Select(x => string.Join(",", Escape(x.Module), Escape(x.Server), Escape(x.Subject), Escape(x.Severity.ToString()), Escape(x.Message))));
        File.WriteAllLines(path, lines, Encoding.UTF8);
    }

    public static string Escape(string? value)
    {
        value ??= "";
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.StartsWith(' ') || value.EndsWith(' '))
        {
            return '"' + value.Replace("\"", "\"\"") + '"';
        }

        return value;
    }
}
