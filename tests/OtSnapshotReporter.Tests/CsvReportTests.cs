namespace OtSnapshotReporter.Tests;

public sealed class CsvReportTests
{
    [Fact] public void Escape_NullString_ReturnsEmpty() => Assert.Equal("", CsvReport.Escape(null));
    [Fact] public void Escape_PlainString_ReturnsSame() => Assert.Equal("abc", CsvReport.Escape("abc"));
    [Fact] public void Escape_ContainsComma_WrapsInQuotes() => Assert.Equal("\"a,b\"", CsvReport.Escape("a,b"));
    [Fact] public void Escape_ContainsDoubleQuote_DoublesQuotes() => Assert.Equal("\"a\"\"b\"", CsvReport.Escape("a\"b"));
    [Fact] public void Escape_ContainsNewline_WrapsInQuotes() => Assert.Equal("\"a\nb\"", CsvReport.Escape("a\nb"));
    [Fact] public void Escape_ContainsCarriageReturn_WrapsInQuotes() => Assert.Equal("\"a\rb\"", CsvReport.Escape("a\rb"));
    [Fact] public void Escape_LeadingWhitespace_WrapsInQuotes() => Assert.Equal("\" abc\"", CsvReport.Escape(" abc"));
    [Fact] public void Escape_TrailingWhitespace_WrapsInQuotes() => Assert.Equal("\"abc \"", CsvReport.Escape("abc "));

    [Fact] public void WriteFindings_ProducesValidCsv()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        CsvReport.WriteFindings(path, [Finding.Create("m", "s", "x", Severity.High, "hello, world")]);
        var csv = File.ReadAllText(path);
        Assert.Contains("\"hello, world\"", csv);
    }

    [Fact] public void WriteFindings_EmptyList_ProducesHeaderOnly()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        CsvReport.WriteFindings(path, []);
        var lines = File.ReadAllLines(path);
        File.Delete(path);
        Assert.Single(lines);
    }

    [Fact] public void WriteFindings_UsesStableSeverityAndIdentityOrdering()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".csv");
        try
        {
            CsvReport.WriteFindings(path, [
                Finding.Create("services", "SRV02", "B", Severity.High, "z"),
                Finding.Create("services", "SRV01", "A", Severity.High, "a"),
                Finding.Create("disk", "SRV01", "C:", Severity.Critical, "full")
            ]);

            var lines = File.ReadAllLines(path);
            Assert.Contains("disk,SRV01,C:,Critical,full", lines[1]);
            Assert.Contains("services,SRV01,A,High,a", lines[2]);
            Assert.Contains("services,SRV02,B,High,z", lines[3]);
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
