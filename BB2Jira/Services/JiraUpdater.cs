using System.Globalization;
using System.Text.RegularExpressions;
using BB2Jira.Models.Mapping;
using BB2Jira.Services.Jira;
using Microsoft.Extensions.Logging;

namespace BB2Jira.Services;

/// <summary>
/// Reads import.csv and updates Jira issues via the API:
///   Phase 1 – builds a Bitbucket issue ID → Jira key map by searching project descriptions.
///   Phase 2 – for each CSV row: optionally updates status and adds missing comments.
/// Supports resume: the last successfully processed Bitbucket issue ID is persisted to a
/// progress file so a subsequent run can skip already-processed rows.
/// </summary>
public sealed class JiraUpdater
{
    // Column indices in the CSV (0-based), matching CsvGenerator.BaseColumns order.
    private const int ColIssueType        = 0;
    private const int ColSummary          = 1;
    private const int ColStatus           = 3;
    private const int ColBitbucketIssueId = 11;
    private const int FirstCommentCol     = 12;

    private readonly IJiraClient _client;
    private readonly JiraSettings _settings;
    private readonly ILogger _logger;

    public JiraUpdater(IJiraClient client, JiraSettings settings, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _client   = client;
        _settings = settings;
        _logger   = logger;
    }

    /// <summary>
    /// Runs the full update cycle against <paramref name="csvPath"/>.
    /// Returns false when any error occurred (but processing continues on per-issue errors).
    /// </summary>
    public async Task<bool> RunAsync(
        string csvPath,
        string progressPath,
        CancellationToken ct = default)
    {
        // Verify connectivity before doing any work.
        if (!await _client.TestConnectionAsync(ct).ConfigureAwait(false))
        {
            _logger.LogError("Cannot connect to Jira. Check baseUrl, email and apiToken in map.json.");
            return false;
        }

        // Phase 1: build Bitbucket ID → Jira key map.
        _logger.LogInformation("Phase 1: searching Jira project '{Project}' for imported issues…", _settings.ProjectKey);
        var bbIdToJiraKey = await BuildBitbucketIdMapAsync(ct).ConfigureAwait(false);
        _logger.LogInformation("Phase 1 complete: {Count} issue(s) mapped.", bbIdToJiraKey.Count);

        // Load CSV rows (skip header).
        var rows = CsvValidator.ParseCsv(File.ReadAllText(csvPath, System.Text.Encoding.UTF8));
        if (rows.Count < 2)
        {
            _logger.LogWarning("import.csv contains no data rows.");
            return true;
        }

        var dataRows = rows.Skip(1).ToList();

        // Resume: skip rows with Bitbucket Issue ID ≤ lastProcessed.
        var lastProcessed = ReadProgress(progressPath);
        if (lastProcessed > 0)
        {
            _logger.LogInformation("Resuming from Bitbucket Issue ID > {LastProcessed}.", lastProcessed);
        }

        // Phase 2: process each CSV row.
        _logger.LogInformation("Phase 2: updating {Total} issue(s)…", dataRows.Count);
        var stats = new UpdateStats();
        var hasErrors = false;

        foreach (var row in dataRows)
        {
            ct.ThrowIfCancellationRequested();

            if (!TryGetBitbucketId(row, out var bbId))
            {
                continue;
            }

            if (bbId <= lastProcessed)
            {
                _logger.LogDebug("Skipping BB#{BbId} (already processed).", bbId);
                stats.Skipped++;
                continue;
            }

            if (!bbIdToJiraKey.TryGetValue(bbId, out var jiraKey))
            {
                _logger.LogWarning("BB#{BbId}: no matching Jira issue found — skipped.", bbId);
                stats.NotFound++;
                continue;
            }

            _logger.LogDebug("BB#{BbId} → {JiraKey}", bbId, jiraKey);
            var issueOk = true;

            if (_settings.UpdateStatus)
            {
                issueOk &= await UpdateStatusAsync(jiraKey, row, ct).ConfigureAwait(false);
                if (!issueOk)
                {
                    hasErrors = true;
                }
            }

            if (_settings.UpdateComments)
            {
                var commentOk = await UpdateCommentsAsync(jiraKey, row, ct).ConfigureAwait(false);
                if (!commentOk)
                {
                    hasErrors = true;
                }
            }

            stats.Processed++;
            WriteProgress(progressPath, bbId);
        }

        WriteSummary(stats);

        // Delete the progress file on clean completion (no unrecoverable errors).
        if (!hasErrors && File.Exists(progressPath))
        {
            File.Delete(progressPath);
            _logger.LogDebug("Progress file deleted (run completed successfully).");
        }

        return !hasErrors;
    }

    // -------------------------------------------------------------------------
    // Phase 1: build Bitbucket ID → Jira key map
    // -------------------------------------------------------------------------

    private async Task<Dictionary<int, string>> BuildBitbucketIdMapAsync(CancellationToken ct)
    {
        // Escape the repo URL for use in a JQL "~" (contains) search.
        // We search for issues whose description contains the base path of the repo URL.
        var repoPath = _settings.BitbucketRepoUrl.TrimEnd('/');
        var jql = $"project = \"{_settings.ProjectKey}\" AND description ~ \"{repoPath}/issues\" ORDER BY created ASC";

        // Build issue-number extraction pattern: .../issues/42
        var issuePattern = new Regex(
            Regex.Escape(repoPath) + @"/issues/(\d+)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        var map = new Dictionary<int, string>();
        var startAt = 0;
        const int pageSize = 100;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var page = await _client.SearchAsync(jql, startAt, pageSize, ct).ConfigureAwait(false);
            if (page.Count == 0)
            {
                break;
            }

            foreach (var issue in page)
            {
                var match = issuePattern.Match(issue.Description);
                if (match.Success && int.TryParse(match.Groups[1].Value, out var bbId))
                {
                    if (!map.TryAdd(bbId, issue.Key))
                    {
                        _logger.LogWarning(
                            "Duplicate Bitbucket ID {BbId}: already mapped to {Existing}, ignoring {Duplicate}.",
                            bbId, map[bbId], issue.Key);
                    }
                }
            }

            if (page.Count < pageSize)
            {
                break;
            }

            startAt += pageSize;
        }

        return map;
    }

    // -------------------------------------------------------------------------
    // Phase 2a: update status
    // -------------------------------------------------------------------------

    private async Task<bool> UpdateStatusAsync(
        string jiraKey, List<string> row, CancellationToken ct)
    {
        var targetStatus = GetField(row, ColStatus);
        if (string.IsNullOrWhiteSpace(targetStatus))
        {
            _logger.LogDebug("{JiraKey}: Status is empty in CSV — skipping status update.", jiraKey);
            return true;
        }

        List<JiraTransition> transitions;
        try
        {
            transitions = await _client.GetTransitionsAsync(jiraKey, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            _logger.LogError("{JiraKey}: Failed to get transitions: {Message}", jiraKey, ex.Message);
            return false;
        }

        var transition = transitions
            .FirstOrDefault(t => t.Name.Equals(targetStatus, StringComparison.Ordinal));

        if (transition is null)
        {
            _logger.LogError(
                "{JiraKey}: No transition to status '{Status}' is available. Available: {Available}",
                jiraKey, targetStatus,
                string.Join(", ", transitions.Select(t => $"'{t.Name}'")));
            return false;
        }

        try
        {
            await _client.ApplyTransitionAsync(jiraKey, transition.Id, ct).ConfigureAwait(false);
            _logger.LogDebug("{JiraKey}: Status set to '{Status}'.", jiraKey, targetStatus);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            _logger.LogError("{JiraKey}: Failed to apply transition to '{Status}': {Message}",
                jiraKey, targetStatus, ex.Message);
            return false;
        }
    }

    // -------------------------------------------------------------------------
    // Phase 2b: add missing comments
    // -------------------------------------------------------------------------

    private async Task<bool> UpdateCommentsAsync(
        string jiraKey, List<string> row, CancellationToken ct)
    {
        // Collect CSV comment fields (columns 12+), skipping empty ones.
        var csvComments = new List<ParsedComment>();
        for (var i = FirstCommentCol; i < row.Count; i++)
        {
            var field = row[i];
            if (string.IsNullOrWhiteSpace(field))
            {
                continue;
            }

            if (TryParseComment(field, out var parsed))
            {
                csvComments.Add(parsed);
            }
        }

        if (csvComments.Count == 0)
        {
            return true;
        }

        // Get existing comments from Jira (up to 100 — sufficient per design).
        List<JiraComment> existing;
        try
        {
            existing = await _client.GetCommentsAsync(jiraKey, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            _logger.LogError("{JiraKey}: Failed to get comments: {Message}", jiraKey, ex.Message);
            return false;
        }

        // The latest existing comment date is the cut-off point.
        // Comments with a date > latest existing are missing and need to be added.
        var latestExisting = existing.Count > 0
            ? existing.Max(c => c.Created)
            : DateTimeOffset.MinValue;

        var toAdd = csvComments
            .Where(c => c.Date > latestExisting)
            .OrderBy(c => c.Date)
            .ToList();

        foreach (var comment in toAdd)
        {
            ct.ThrowIfCancellationRequested();

            // Resolve accountId to displayName (cached).
            var author = string.IsNullOrWhiteSpace(comment.AccountId)
                ? string.Empty
                : await _client.ResolveDisplayNameAsync(comment.AccountId, ct).ConfigureAwait(false);

            // Format: "date | DisplayName | text"
            var text = string.IsNullOrWhiteSpace(author)
                ? $"{comment.Date:yyyy-MM-dd HH:mm:ss} | {comment.Text}"
                : $"{comment.Date:yyyy-MM-dd HH:mm:ss} | {author} | {comment.Text}";

            try
            {
                await _client.AddCommentAsync(jiraKey, text, ct).ConfigureAwait(false);
                _logger.LogDebug("{JiraKey}: Added comment dated {Date}.", jiraKey, comment.Date);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                _logger.LogError("{JiraKey}: Failed to add comment dated {Date}: {Message}",
                    jiraKey, comment.Date, ex.Message);
                return false;
            }
        }

        return true;
    }

    // -------------------------------------------------------------------------
    // Comment parsing
    // -------------------------------------------------------------------------

    // CSV comment format: "date;accountId;text" (accountId may be empty → "date;;text")
    private static bool TryParseComment(string field, out ParsedComment result)
    {
        result = default;
        var parts = field.Split(';', 3);
        if (parts.Length < 3)
        {
            return false;
        }

        if (!DateTime.TryParseExact(
                parts[0].Trim(),
                "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dt))
        {
            return false;
        }

        result = new ParsedComment(new DateTimeOffset(dt, TimeSpan.Zero), parts[1].Trim(), parts[2]);
        return true;
    }

    private readonly record struct ParsedComment(DateTimeOffset Date, string AccountId, string Text);

    // -------------------------------------------------------------------------
    // Progress file
    // -------------------------------------------------------------------------

    private static int ReadProgress(string path)
    {
        if (!File.Exists(path))
        {
            return 0;
        }

        var line = File.ReadAllLines(path)
            .FirstOrDefault(l => l.StartsWith("last_processed=", StringComparison.Ordinal));

        return line is not null &&
               int.TryParse(line["last_processed=".Length..], out var id) ? id : 0;
    }

    private static void WriteProgress(string path, int lastProcessedId)
    {
        File.WriteAllText(path, $"last_processed={lastProcessedId}\n");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static bool TryGetBitbucketId(List<string> row, out int id)
    {
        id = 0;
        if (row.Count <= ColBitbucketIssueId)
        {
            return false;
        }

        return int.TryParse(row[ColBitbucketIssueId], NumberStyles.Integer,
            CultureInfo.InvariantCulture, out id);
    }

    private static string GetField(List<string> row, int index) =>
        row.Count > index ? row[index].Trim() : string.Empty;

    private void WriteSummary(UpdateStats stats)
    {
        _logger.LogInformation("----- update summary -----");
        _logger.LogInformation("Processed: {Processed}", stats.Processed);
        _logger.LogInformation("Not found in Jira: {NotFound}", stats.NotFound);
        _logger.LogInformation("Skipped (already done): {Skipped}", stats.Skipped);
    }

    private sealed class UpdateStats
    {
        public int Processed { get; set; }
        public int NotFound  { get; set; }
        public int Skipped   { get; set; }
    }
}
