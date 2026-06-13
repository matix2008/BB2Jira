using BB2Jira.Services;
using Xunit;

namespace BB2Jira.Tests;

public class CsvWriterTests
{
    [Fact]
    public void WhenValueHasNoSpecialCharsThenItIsNotQuoted()
    {
        var result = CsvWriter.Escape("simple");

        Assert.Equal("simple", result);
    }

    [Fact]
    public void WhenValueHasCommaThenItIsQuoted()
    {
        var result = CsvWriter.Escape("a,b");

        Assert.Equal("\"a,b\"", result);
    }

    [Fact]
    public void WhenValueHasQuoteThenQuoteIsDoubled()
    {
        var result = CsvWriter.Escape("say \"hi\"");

        Assert.Equal("\"say \"\"hi\"\"\"", result);
    }

    [Fact]
    public void WhenValueHasNewLineThenItIsQuoted()
    {
        var result = CsvWriter.Escape("line1\nline2");

        Assert.Equal("\"line1\nline2\"", result);
    }

    [Fact]
    public void WhenValueIsNullThenResultIsEmpty()
    {
        var result = CsvWriter.Escape(null);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void WhenRowWrittenThenFieldsAreCommaSeparated()
    {
        var writer = new CsvWriter();

        writer.WriteRow(new[] { "a", "b", "c" });

        Assert.Equal("a,b,c\r\n", writer.ToString());
    }
}
