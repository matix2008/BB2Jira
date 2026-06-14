using System.Globalization;
using System.Text;
using BB2Jira.Models.Bitbucket;
using BB2Jira.Models.Mapping;
using Microsoft.Extensions.Logging;

namespace BB2Jira.Services;

/// <summary>
/// Validates an existing import.csv file produced by <see cref="CsvGenerator"/>.
/// </summary>
public static class CsvValidator
{
    private const string DateFormat = "yyyy-MM-dd HH:mm:ss";

    /// <summary>Required column names that must appear in the CSV header in this exact order.</summary>
    private static readonly string[] RequiredColumns =
    {
        "Issue Type", "Summary", "Description", "Status", "Priority",
        "Reporter", "Assignee", "Created", "Updated", "Fix Version/s",
        "Bitbucket Milestone", "Bitbucket Issue ID",
    };

    /// <summary>
    /// Validates <paramref name="csvPath"/> and returns <c>true</c> when no errors are found.
    /// Warnings do not affect the return value.
    /// Optionally cross-references the CSV against the Bitbucket export (<paramref name="export"/>)
    /// and the mapping file (<paramref name="map"/>).
    /// </summary>
    public static bool Validate(
        string csvPath,
        ILogger logger,
        BitbucketExport? export = null,
        MapFile? map = null)
    {
        ArgumentNullException.ThrowIfNull(csvPath);
        ArgumentNullException.ThrowIfNull(logger);

        logger.LogInformation("----- import.csv validation -----");
        logger.LogInformation("File: {CsvPath}", csvPath);

        var stats = new ValidationStats();

        // Check 1: file exists and is readable UTF-8.
        (var e0, var w0) = (stats.ErrorCount, stats.WarningCount);
        var fileOk = TryReadFile(csvPath, logger, stats, out var rawText);
        ReportCheck(logger, 1, "File exists and readable UTF-8", stats.ErrorCount - e0, stats.WarningCount - w0);
        if (!fileOk)
        {
            WriteSummary(logger, stats, hasErrors: true);
            return false;
        }

        // Check 2: parse CSV; verify balanced quotes and uniform column count.
        (e0, w0) = (stats.ErrorCount, stats.WarningCount);
        var parseOk = TryParseCsv(rawText!, logger, stats, out var rows);
        ReportCheck(logger, 2, "CSV syntax (RFC 4180, balanced quotes, uniform columns)", stats.ErrorCount - e0, stats.WarningCount - w0);
        if (!parseOk)
        {
            WriteSummary(logger, stats, hasErrors: true);
            return false;
        }

        // Check 3: header row must contain all required columns in the right order.
        (e0, w0) = (stats.ErrorCount, stats.WarningCount);
        var headerOk = ValidateHeader(rows![0], logger, stats, out var colIndex);
        ReportCheck(logger, 3, "Header columns present and in correct order", stats.ErrorCount - e0, stats.WarningCount - w0);
        if (!headerOk)
        {
            WriteSummary(logger, stats, hasErrors: true);
            return false;
        }

        var dataRows = rows.Skip(1).ToList();
        logger.LogInformation("Data rows: {RowCount}", dataRows.Count);

        // Check 4: required fields must not be empty (Issue Type, Summary, Status → error).
        (e0, w0) = (stats.ErrorCount, stats.WarningCount);
        ValidateRequiredFields(dataRows, colIndex, logger, stats);
        ReportCheck(logger, 4, "Required fields not empty (Issue Type, Summary, Status)", stats.ErrorCount - e0, stats.WarningCount - w0);

        // Check 5: date fields must match the expected format.
        (e0, w0) = (stats.ErrorCount, stats.WarningCount);
        ValidateDates(dataRows, colIndex, logger, stats);
        ReportCheck(logger, 5, "Date format (Created, Updated) matches yyyy-MM-dd HH:mm:ss", stats.ErrorCount - e0, stats.WarningCount - w0);

        // Check 6: Bitbucket Issue ID must be unique.
        (e0, w0) = (stats.ErrorCount, stats.WarningCount);
        ValidateUniqueIds(dataRows, colIndex, logger, stats);
        ReportCheck(logger, 6, "Bitbucket Issue ID uniqueness", stats.ErrorCount - e0, stats.WarningCount - w0);

        // Check 7 (when export is provided): every export issue must appear in the CSV.
        (e0, w0) = (stats.ErrorCount, stats.WarningCount);
        if (export is not null)
        {
            ValidateAgainstExport(dataRows, colIndex, export, logger, stats);
            ReportCheck(logger, 7, "All export issues present in CSV", stats.ErrorCount - e0, stats.WarningCount - w0);
        }
        else
        {
            ReportCheck(logger, 7, "All export issues present in CSV", 0, 0, skipped: true);
        }

        // Check 8 (when map is provided): Issue Type must be a valid value from map.json kind.
        (e0, w0) = (stats.ErrorCount, stats.WarningCount);
        if (map is not null)
        {
            ValidateIssueTypes(dataRows, colIndex, map, logger, stats);
            ReportCheck(logger, 8, "Issue Type values in map.json kind", stats.ErrorCount - e0, stats.WarningCount - w0);
        }
        else
        {
            ReportCheck(logger, 8, "Issue Type values in map.json kind", 0, 0, skipped: true);
        }

        var hasErrors = stats.ErrorCount > 0;
        WriteSummary(logger, stats, hasErrors);
        return !hasErrors;
    }

    // -------------------------------------------------------------------------
    // Check 1: file read
    // -------------------------------------------------------------------------

    private static bool TryReadFile(string path, ILogger logger, ValidationStats stats, out string? text)
    {
        text = null;
        if (!File.Exists(path))
        {
            logger.LogError("File not found: {Path}", path);
            stats.ErrorCount++;
            return false;
        }

        try
        {
            // Accept UTF-8 with or without BOM.
            text = File.ReadAllText(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            logger.LogDebug("File read: {Length} characters.", text.Length);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError("Cannot read file: {Message}", ex.Message);
            stats.ErrorCount++;
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Check 2: CSV syntax (balanced quotes, uniform column count)
    // -------------------------------------------------------------------------

    private static bool TryParseCsv(
        string text,
        ILogger logger,
        ValidationStats stats,
        out List<List<string>>? rows)
    {
        rows = null;

        if (string.IsNullOrWhiteSpace(text))
        {
            logger.LogError("The CSV file is empty.");
            stats.ErrorCount++;
            return false;
        }

        try
        {
            var parsed = ParseCsv(text);

            if (parsed.Count == 0)
            {
                logger.LogError("The CSV file is empty.");
                stats.ErrorCount++;
                return false;
            }

            var headerWidth = parsed[0].Count;
            var unevenRows = new List<int>();

            for (var i = 1; i < parsed.Count; i++)
            {
                if (parsed[i].Count != headerWidth)
                {
                    unevenRows.Add(i + 1); // 1-based for reporting
                }
            }

            if (unevenRows.Count > 0)
            {
                logger.LogError(
                    "Column count mismatch on {Count} row(s): {Rows}",
                    unevenRows.Count,
                    string.Join(", ", unevenRows));
                stats.ErrorCount++;
                return false;
            }

            rows = parsed;
            logger.LogDebug("CSV parsed: {RowCount} total rows (including header).", parsed.Count);
            return true;
        }
        catch (FormatException ex)
        {
            logger.LogError("CSV syntax error: {Message}", ex.Message);
            stats.ErrorCount++;
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Check 3: header columns
    // -------------------------------------------------------------------------

    private static bool ValidateHeader(
        List<string> header,
        ILogger logger,
        ValidationStats stats,
        out Dictionary<string, int> colIndex)
    {
        colIndex = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < header.Count; i++)
        {
            colIndex[header[i]] = i;
        }

        var missing = new List<string>();
        var wrongOrder = new List<string>();

        for (var i = 0; i < RequiredColumns.Length; i++)
        {
            var col = RequiredColumns[i];
            if (!colIndex.TryGetValue(col, out var actualIdx))
            {
                missing.Add(col);
            }
            else if (actualIdx != i)
            {
                wrongOrder.Add($"'{col}' at position {actualIdx} (expected {i})");
            }
        }

        var ok = true;
        if (missing.Count > 0)
        {
            logger.LogError("Missing required columns: {Columns}", string.Join(", ", missing));
            stats.ErrorCount++;
            ok = false;
        }

        if (wrongOrder.Count > 0)
        {
            logger.LogError("Columns in wrong order: {Columns}", string.Join("; ", wrongOrder));
            stats.ErrorCount++;
            ok = false;
        }

        if (ok)
        {
            logger.LogDebug("Header validated: all {Count} required columns present and in order.", RequiredColumns.Length);
        }

        return ok;
    }

    // -------------------------------------------------------------------------
    // Check 4: required fields not empty (error-level)
    // -------------------------------------------------------------------------

    private static void ValidateRequiredFields(
        List<List<string>> dataRows,
        Dictionary<string, int> colIndex,
        ILogger logger,
        ValidationStats stats)
    {
        string[] required = ["Issue Type", "Summary", "Status"];

        foreach (var col in required)
        {
            if (!colIndex.TryGetValue(col, out var idx))
            {
                continue; // already reported in header check
            }

            for (var r = 0; r < dataRows.Count; r++)
            {
                var value = dataRows[r].Count > idx ? dataRows[r][idx] : string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    logger.LogError("Row {Row}: required field '{Column}' is empty.", r + 2, col);
                    stats.ErrorCount++;
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Check 5: date format
    // -------------------------------------------------------------------------

    private static void ValidateDates(
        List<List<string>> dataRows,
        Dictionary<string, int> colIndex,
        ILogger logger,
        ValidationStats stats)
    {
        string[] dateColumns = ["Created", "Updated"];

        foreach (var col in dateColumns)
        {
            if (!colIndex.TryGetValue(col, out var idx))
            {
                continue;
            }

            for (var r = 0; r < dataRows.Count; r++)
            {
                var value = dataRows[r].Count > idx ? dataRows[r][idx] : string.Empty;
                if (string.IsNullOrWhiteSpace(value))
                {
                    // An empty date field is warned rather than errored.
                    logger.LogWarning("Row {Row}: date field '{Column}' is empty.", r + 2, col);
                    stats.WarningCount++;
                    continue;
                }

                if (!DateTime.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                {
                    logger.LogWarning(
                        "Row {Row}: '{Column}' value '{Value}' does not match expected format '{Format}'.",
                        r + 2, col, value, DateFormat);
                    stats.WarningCount++;
                }
            }
        }
    }

    // -------------------------------------------------------------------------
    // Check 6: unique Bitbucket Issue ID
    // -------------------------------------------------------------------------

    private static void ValidateUniqueIds(
        List<List<string>> dataRows,
        Dictionary<string, int> colIndex,
        ILogger logger,
        ValidationStats stats)
    {
        if (!colIndex.TryGetValue("Bitbucket Issue ID", out var idx))
        {
            return;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal); // id -> first row number
        var duplicates = new List<string>();

        for (var r = 0; r < dataRows.Count; r++)
        {
            var id = dataRows[r].Count > idx ? dataRows[r][idx] : string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            if (!seen.TryAdd(id, r + 2))
            {
                duplicates.Add($"ID '{id}' in rows {seen[id]} and {r + 2}");
            }
        }

        if (duplicates.Count > 0)
        {
            foreach (var dup in duplicates)
            {
                logger.LogWarning("Duplicate Bitbucket Issue ID: {Duplicate}", dup);
                stats.WarningCount++;
            }
        }
        else
        {
            logger.LogDebug("All {Count} Bitbucket Issue IDs are unique.", seen.Count);
        }
    }

    // -------------------------------------------------------------------------
    // Check 7: cross-reference with Bitbucket export
    // -------------------------------------------------------------------------

    private static void ValidateAgainstExport(
        List<List<string>> dataRows,
        Dictionary<string, int> colIndex,
        BitbucketExport export,
        ILogger logger,
        ValidationStats stats)
    {
        if (!colIndex.TryGetValue("Bitbucket Issue ID", out var idx))
        {
            return;
        }

        var csvIds = dataRows
            .Select(r => r.Count > idx ? r[idx] : string.Empty)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .ToHashSet(StringComparer.Ordinal);

        var missing = export.Issues
            .Select(i => i.Id.ToString(CultureInfo.InvariantCulture))
            .Where(id => !csvIds.Contains(id))
            .OrderBy(id => int.TryParse(id, out var n) ? n : int.MaxValue)
            .ToList();

        if (missing.Count > 0)
        {
            logger.LogWarning(
                "{Count} Bitbucket issue(s) from the export are absent from the CSV: {Ids}",
                missing.Count,
                string.Join(", ", missing));
            stats.WarningCount++;
        }
        else
        {
            logger.LogInformation(
                "All {Count} Bitbucket export issues are present in the CSV.",
                export.Issues.Count);
        }
    }

    // -------------------------------------------------------------------------
    // Check 8: Issue Type values against map.json kind
    // -------------------------------------------------------------------------

    private static void ValidateIssueTypes(
        List<List<string>> dataRows,
        Dictionary<string, int> colIndex,
        MapFile map,
        ILogger logger,
        ValidationStats stats)
    {
        if (!colIndex.TryGetValue("Issue Type", out var idx))
        {
            return;
        }

        // Valid values are the unique non-empty mapped values in map.Kind.
        var validTypes = map.Kind.Values
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var badRows = new List<(int Row, string Value)>();

        for (var r = 0; r < dataRows.Count; r++)
        {
            var value = dataRows[r].Count > idx ? dataRows[r][idx] : string.Empty;
            if (!string.IsNullOrWhiteSpace(value) && !validTypes.Contains(value))
            {
                badRows.Add((r + 2, value));
            }
        }

        if (badRows.Count > 0)
        {
            foreach (var (row, value) in badRows)
            {
                logger.LogWarning(
                    "Row {Row}: Issue Type '{Value}' is not a mapped value in map.json kind.",
                    row, value);
                stats.WarningCount++;
            }
        }
        else
        {
            logger.LogDebug("All Issue Type values match map.json kind mappings.");
        }
    }

    // -------------------------------------------------------------------------
    // Per-check result line
    // -------------------------------------------------------------------------

    /// <summary>
    /// Logs a single summary line for check <paramref name="number"/> of 8
    /// after all per-issue messages for that check have been emitted.
    /// </summary>
    private static void ReportCheck(
        ILogger logger,
        int number,
        string description,
        int errorsDelta,
        int warnsDelta,
        bool skipped = false)
    {
        const int total = 8;
        if (skipped)
        {
            logger.LogInformation("[{Number}/{Total}] {Description} -- SKIPPED", number, total, description);
            return;
        }

        if (errorsDelta > 0)
        {
            logger.LogError("[{Number}/{Total}] {Description} -- {Count} error(s)", number, total, description, errorsDelta);
        }
        else if (warnsDelta > 0)
        {
            logger.LogWarning("[{Number}/{Total}] {Description} -- {Count} warning(s)", number, total, description, warnsDelta);
        }
        else
        {
            logger.LogInformation("[{Number}/{Total}] {Description} -- OK", number, total, description);
        }
    }

    // -------------------------------------------------------------------------
    // Summary
    // -------------------------------------------------------------------------

    private static void WriteSummary(ILogger logger, ValidationStats stats, bool hasErrors)
    {
        logger.LogInformation("----- validation summary -----");
        logger.LogInformation("Errors: {ErrorCount}", stats.ErrorCount);
        logger.LogInformation("Warnings: {WarningCount}", stats.WarningCount);

        if (hasErrors)
        {
            logger.LogError("Validation FAILED.");
        }
        else
        {
            logger.LogInformation("Validation PASSED.");
        }
    }

    // -------------------------------------------------------------------------
    // RFC 4180 CSV parser
    // -------------------------------------------------------------------------

    /// <summary>
    /// Parses CSV text (RFC 4180) into a list of rows, each a list of field values.
    /// Quoted fields may span multiple lines.
    /// </summary>
    /// <exception cref="FormatException">Thrown when quoted fields are not balanced.</exception>
    public static List<List<string>> ParseCsv(string text)
    {
        var rows = new List<List<string>>();
        var pos = 0;
        var len = text.Length;

        while (pos <= len)
        {
            var row = new List<string>();

            // Parse all fields in this row.
            while (true)
            {
                var (field, next, eol) = ReadField(text, pos, len);
                row.Add(field);
                pos = next;

                if (eol)
                {
                    break;
                }
            }

            // Skip the final empty "row" that appears after a trailing CRLF.
            if (pos > len && rows.Count > 0 && row.Count == 1 && row[0].Length == 0)
            {
                break;
            }

            rows.Add(row);
        }

        return rows;
    }

    private static (string Field, int Next, bool Eol) ReadField(string text, int pos, int len)
    {
        if (pos > len)
        {
            return (string.Empty, pos + 1, true);
        }

        if (pos == len)
        {
            return (string.Empty, pos + 1, true);
        }

        if (text[pos] == '"')
        {
            return ReadQuotedField(text, pos, len);
        }

        return ReadPlainField(text, pos, len);
    }

    private static (string Field, int Next, bool Eol) ReadPlainField(string text, int pos, int len)
    {
        var sb = new StringBuilder();

        while (pos < len)
        {
            var ch = text[pos];
            if (ch == ',')
            {
                return (sb.ToString(), pos + 1, false);
            }

            if (ch == '\r' && pos + 1 < len && text[pos + 1] == '\n')
            {
                return (sb.ToString(), pos + 2, true);
            }

            if (ch == '\n')
            {
                return (sb.ToString(), pos + 1, true);
            }

            sb.Append(ch);
            pos++;
        }

        return (sb.ToString(), pos, true);
    }

    private static (string Field, int Next, bool Eol) ReadQuotedField(string text, int pos, int len)
    {
        // Skip opening quote.
        pos++;
        var sb = new StringBuilder();
        var closed = false;

        while (pos < len)
        {
            var ch = text[pos];

            if (ch == '"')
            {
                // Doubled quote = escaped quote character.
                if (pos + 1 < len && text[pos + 1] == '"')
                {
                    sb.Append('"');
                    pos += 2;
                    continue;
                }

                // Closing quote.
                pos++;
                closed = true;
                break;
            }

            sb.Append(ch);
            pos++;
        }

        if (!closed)
        {
            throw new FormatException("Unbalanced quotes in CSV field.");
        }

        // After closing quote: expect comma, CRLF, LF, or end.
        if (pos == len)
        {
            return (sb.ToString(), pos, true);
        }

        var after = text[pos];
        if (after == ',')
        {
            return (sb.ToString(), pos + 1, false);
        }

        if (after == '\r' && pos + 1 < len && text[pos + 1] == '\n')
        {
            return (sb.ToString(), pos + 2, true);
        }

        if (after == '\n')
        {
            return (sb.ToString(), pos + 1, true);
        }

        return (sb.ToString(), pos, true);
    }

    // -------------------------------------------------------------------------
    // Internal stats
    // -------------------------------------------------------------------------

    private sealed class ValidationStats
    {
        public int ErrorCount { get; set; }

        public int WarningCount { get; set; }
    }
}
