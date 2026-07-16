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
}
