using System.Globalization;
using BB2Jira.Models.Bitbucket;
using BB2Jira.Models.Mapping;
using BB2Jira.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BB2Jira.Tests;

/// <summary>Tests for import.csv validation in <see cref="CsvValidator"/>.</summary>
public class CsvValidatorTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private const string ValidHeader =
        "Issue Type,Summary,Description,Status,Priority,Reporter,Assignee,Created,Updated,Fix Version/s,Bitbucket Milestone,Bitbucket Issue ID";

    private static string MakeRow(
        string issueType = "Bug",
        string summary = "Test issue",
        string description = "desc",
        string status = "Open",
        string priority = "Medium",
        string reporter = "user@example.com",
        string assignee = "",
        string created = "2024-01-15 10:00:00",
        string updated = "2024-01-15 11:00:00",
        string fixVersion = "",
        string milestone = "",
        string bbId = "1")
    {
        return $"{issueType},{summary},{description},{status},{priority},{reporter},{assignee},{created},{updated},{fixVersion},{milestone},{bbId}";
    }

    private static string MakeCsv(params string[] dataRows)
    {
        var lines = new[] { ValidHeader }.Concat(dataRows);
        return string.Join("\r\n", lines) + "\r\n";
    }

    /// <summary>Writes CSV text to a temp file and returns its path.</summary>
    private static string WriteTempCsv(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, content, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        return path;
    }

    // Captures log messages so tests can assert on validation output.
    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception)));
        }

        public bool HasError(string fragment) =>
            Entries.Any(e => e.Level == LogLevel.Error && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        public bool HasWarning(string fragment) =>
            Entries.Any(e => e.Level == LogLevel.Warning && e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    // -------------------------------------------------------------------------
    // ParseCsv unit tests (internal parser)
    // -------------------------------------------------------------------------

    [Fact]
    public void ParseCsv_SingleRowNoQuotes_ReturnsCorrectFields()
    {
        var rows = CsvValidator.ParseCsv("a,b,c\r\n");

        Assert.Single(rows);
        Assert.Equal(new[] { "a", "b", "c" }, rows[0]);
    }

    [Fact]
    public void ParseCsv_QuotedFieldWithComma_PreservesComma()
    {
        var rows = CsvValidator.ParseCsv("\"a,b\",c\r\n");

        Assert.Single(rows);
        Assert.Equal("a,b", rows[0][0]);
        Assert.Equal("c", rows[0][1]);
    }

    [Fact]
    public void ParseCsv_QuotedFieldWithCrLf_SpansTwoLines()
    {
        var rows = CsvValidator.ParseCsv("\"a\r\nb\",c\r\n");

        Assert.Single(rows);
        Assert.Equal("a\r\nb", rows[0][0]);
    }

    [Fact]
    public void ParseCsv_DoubledQuoteInsideQuotedField_UnescapesQuote()
    {
        var rows = CsvValidator.ParseCsv("\"say \"\"hello\"\"\",next\r\n");

        Assert.Single(rows);
        Assert.Equal("say \"hello\"", rows[0][0]);
    }

    [Fact]
    public void ParseCsv_UnbalancedQuote_ThrowsFormatException()
    {
        Assert.Throws<FormatException>(() => CsvValidator.ParseCsv("\"unclosed,field\r\n"));
    }

    [Fact]
    public void ParseCsv_MultipleRows_ReturnsCorrectRowCount()
    {
        var rows = CsvValidator.ParseCsv("a,b\r\nc,d\r\ne,f\r\n");

        Assert.Equal(3, rows.Count);
    }

    // -------------------------------------------------------------------------
    // Check 1: file not found
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_FileNotFound_ReturnsFalseWithError()
    {
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate("nonexistent_file_xyz.csv", logger);

        Assert.False(result);
        Assert.True(logger.HasError("not found"));
    }

    // -------------------------------------------------------------------------
    // Check 2: CSV syntax
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_EmptyCsvFile_ReturnsFalseWithError()
    {
        var path = WriteTempCsv(string.Empty);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.False(result);
        Assert.True(logger.HasError("empty"));
    }

    [Fact]
    public void Validate_UnevenColumnCount_ReturnsFalseWithError()
    {
        // Header has 12 columns; data row has 11.
        var csv = ValidHeader + "\r\nBug,Summary,desc,Open,Medium,rep,,2024-01-01 00:00:00,2024-01-01 00:00:00,,\r\n";
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.False(result);
        Assert.True(logger.HasError("mismatch"));
    }

    // -------------------------------------------------------------------------
    // Check 3: header columns
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_MissingRequiredColumn_ReturnsFalseWithError()
    {
        // Remove "Status" from the header.
        var badHeader = "Issue Type,Summary,Description,Priority,Reporter,Assignee,Created,Updated,Fix Version/s,Bitbucket Milestone,Bitbucket Issue ID";
        var csv = badHeader + "\r\n";
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.False(result);
        Assert.True(logger.HasError("Status"));
    }

    [Fact]
    public void Validate_ColumnsInWrongOrder_ReturnsFalseWithError()
    {
        // Swap Summary and Issue Type.
        var badHeader = "Summary,Issue Type,Description,Status,Priority,Reporter,Assignee,Created,Updated,Fix Version/s,Bitbucket Milestone,Bitbucket Issue ID";
        var csv = badHeader + "\r\n";
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.False(result);
        Assert.True(logger.HasError("order"));
    }

    // -------------------------------------------------------------------------
    // Check 4: required fields not empty (error-level)
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_EmptyIssueType_ReturnsFalseWithError()
    {
        var csv = MakeCsv(MakeRow(issueType: ""));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.False(result);
        Assert.True(logger.HasError("Issue Type"));
    }

    [Fact]
    public void Validate_EmptySummary_ReturnsFalseWithError()
    {
        var csv = MakeCsv(MakeRow(summary: ""));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.False(result);
        Assert.True(logger.HasError("Summary"));
    }

    [Fact]
    public void Validate_EmptyStatus_ReturnsFalseWithError()
    {
        var csv = MakeCsv(MakeRow(status: ""));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.False(result);
        Assert.True(logger.HasError("Status"));
    }

    // -------------------------------------------------------------------------
    // Check 5: date format (warning-level)
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_InvalidCreatedDate_ReturnsWithWarning()
    {
        var csv = MakeCsv(MakeRow(created: "15-01-2024"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.True(result);
        Assert.True(logger.HasWarning("Created"));
    }

    [Fact]
    public void Validate_InvalidUpdatedDate_ReturnsWithWarning()
    {
        var csv = MakeCsv(MakeRow(updated: "not-a-date"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.True(result);
        Assert.True(logger.HasWarning("Updated"));
    }

    [Fact]
    public void Validate_ValidDates_PassesWithoutWarnings()
    {
        var csv = MakeCsv(MakeRow(
            created: "2024-01-15 10:00:00",
            updated: "2024-01-15 11:00:00"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.True(result);
        Assert.False(logger.HasWarning("Created"));
        Assert.False(logger.HasWarning("Updated"));
    }

    // -------------------------------------------------------------------------
    // Check 6: unique Bitbucket Issue ID (warning-level)
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_DuplicateBitbucketIssueId_ReturnsWithWarning()
    {
        var csv = MakeCsv(MakeRow(bbId: "42"), MakeRow(bbId: "42"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.True(result);
        Assert.True(logger.HasWarning("42"));
    }

    [Fact]
    public void Validate_UniqueIds_PassesWithoutWarnings()
    {
        var csv = MakeCsv(MakeRow(bbId: "1"), MakeRow(bbId: "2"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        CsvValidator.Validate(path, logger);

        Assert.False(logger.HasWarning("Duplicate"));
    }

    // -------------------------------------------------------------------------
    // Check 7: cross-reference with export (warning-level)
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_ExportIssueAbsentFromCsv_ReturnsWithWarning()
    {
        var csv = MakeCsv(MakeRow(bbId: "1"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var export = new BitbucketExport
        {
            Issues = new List<BitbucketIssue>
            {
                new() { Id = 1 },
                new() { Id = 2 }, // this one is missing from CSV
            },
        };

        var result = CsvValidator.Validate(path, logger, export: export);

        Assert.True(result);
        Assert.True(logger.HasWarning("absent"));
    }

    [Fact]
    public void Validate_AllExportIssuesPresentInCsv_PassesWithoutWarning()
    {
        var csv = MakeCsv(MakeRow(bbId: "1"), MakeRow(bbId: "2", summary: "Second"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var export = new BitbucketExport
        {
            Issues = new List<BitbucketIssue>
            {
                new() { Id = 1 },
                new() { Id = 2 },
            },
        };

        var result = CsvValidator.Validate(path, logger, export: export);

        Assert.True(result);
        Assert.False(logger.HasWarning("absent"));
    }

    // -------------------------------------------------------------------------
    // Check 8: Issue Type vs map.json kind (warning-level)
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_IssueTypeNotInMapKind_ReturnsWithWarning()
    {
        var csv = MakeCsv(MakeRow(issueType: "Epic"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var map = new MapFile();
        map.Kind["bug"] = "Bug";
        map.Kind["task"] = "Task";

        var result = CsvValidator.Validate(path, logger, map: map);

        Assert.True(result);
        Assert.True(logger.HasWarning("Epic"));
    }

    [Fact]
    public void Validate_IssueTypeInMapKind_PassesWithoutWarning()
    {
        var csv = MakeCsv(MakeRow(issueType: "Bug"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var map = new MapFile();
        map.Kind["bug"] = "Bug";

        var result = CsvValidator.Validate(path, logger, map: map);

        Assert.True(result);
        Assert.False(logger.HasWarning("Bug"));
    }

    // -------------------------------------------------------------------------
    // Happy-path: valid CSV with all optional checks enabled
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_ValidCsvWithAllChecks_ReturnsTrueNoErrorsNoWarnings()
    {
        var csv = MakeCsv(MakeRow(issueType: "Bug", bbId: "1"), MakeRow(issueType: "Task", bbId: "2", summary: "Task issue"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var export = new BitbucketExport
        {
            Issues = new List<BitbucketIssue> { new() { Id = 1 }, new() { Id = 2 } },
        };

        var map = new MapFile();
        map.Kind["bug"] = "Bug";
        map.Kind["task"] = "Task";

        var result = CsvValidator.Validate(path, logger, export, map);

        Assert.True(result);
        Assert.DoesNotContain(logger.Entries, e => e.Level is LogLevel.Error or LogLevel.Warning);
    }

    // -------------------------------------------------------------------------
    // Exit-code contract: errors produce false, warnings produce true
    // -------------------------------------------------------------------------

    [Fact]
    public void Validate_WithOnlyWarnings_ReturnsTrue()
    {
        // Duplicate ID causes a warning only.
        var csv = MakeCsv(MakeRow(bbId: "1"), MakeRow(bbId: "1"));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.True(result);
    }

    [Fact]
    public void Validate_WithErrors_ReturnsFalse()
    {
        // Empty Status triggers an error.
        var csv = MakeCsv(MakeRow(status: ""));
        var path = WriteTempCsv(csv);
        var logger = new CapturingLogger();

        var result = CsvValidator.Validate(path, logger);

        Assert.False(result);
    }
}
