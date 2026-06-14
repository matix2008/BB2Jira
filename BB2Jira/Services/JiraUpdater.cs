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
        if (bbIdToJiraKey is null)
        {
            _logger.LogError("Phase 1 failed. Aborting.");
            return false;
        }

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

        var watermarkBroken = false;

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
                var (ok, changed) = await UpdateStatusAsync(jiraKey, row, ct).ConfigureAwait(false);
                issueOk &= ok;
                if (changed)
                {
                    stats.StatusUpdated++;
                }
            }

            if (_settings.UpdateComments)
            {
                var (ok, added) = await UpdateCommentsAsync(jiraKey, row, ct).ConfigureAwait(false);
                issueOk &= ok;
                stats.CommentsAdded += added;
            }

            if (issueOk)
            {
                stats.Processed++;

                // Advance the resume watermark only while every issue so far has succeeded.
                // Once an issue fails, stop advancing so the next run retries from that issue
                // instead of skipping it.
                if (!watermarkBroken)
                {
                    WriteProgress(progressPath, bbId);
                }
            }
            else
            {
                stats.Failed++;
                hasErrors = true;
                watermarkBroken = true;
            }
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

    private async Task<Dictionary<int, string>?> BuildBitbucketIdMapAsync(CancellationToken ct)
    {
        // Phase 1 links a Bitbucket issue ID to a Jira key using one of two strategies,
        // controlled by the "matchBy" setting in map.json:
        //   serviceBlock — matches the "Bitbucket Issue ID: {id}" block written by this
        //                  utility's CSV import.
        //   url          — matches the "{bitbucketRepoUrl}/issues/{id}" URL written by
        //                  Jira's native Bitbucket importer.
        string searchPhrase;
        Regex issuePattern;

        if (_settings.MatchByIssueUrl)
        {
            var repoUrl = _settings.BitbucketRepoUrl.TrimEnd('/');

            // The regex matches the full issue URL (with scheme) written by Jira's importer,
            // e.g. https://bitbucket.org/org/repo/issues/1637/...
            issuePattern = new Regex(
                Regex.Escape(repoUrl) + @"/issues/(\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);

            // For JQL, strip the scheme so the phrase does not contain Lucene-reserved
            // characters (':' and '//'). The regex above does the precise filtering.
            searchPhrase = $"{StripScheme(repoUrl)}/issues";
        }
        else
        {
            // Default: the service block written by CsvGenerator. This avoids URL-escaping
            // issues in JQL and does not depend on bitbucketRepoUrl.
            searchPhrase = "Bitbucket Issue ID";
            issuePattern = new Regex(
                @"Bitbucket Issue ID[:\s]+(\d+)",
                RegexOptions.Compiled | RegexOptions.IgnoreCase);
        }

        var jql = $"project = \"{_settings.ProjectKey}\" AND description ~ \"{searchPhrase}\" ORDER BY created ASC";
        _logger.LogDebug("Phase 1 JQL (matchBy={Strategy}): {Jql}", _settings.MatchBy, jql);

        var map = new Dictionary<int, string>();
        string? nextPageToken = null;
        const int pageSize = 100;

        while (true)
        {
            ct.ThrowIfCancellationRequested();

            JiraSearchPage page;
            try
            {
                page = await _client.SearchAsync(jql, pageSize, nextPageToken, ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(
                    "Phase 1 search failed (nextPageToken={Token}): {Message}",
                    nextPageToken ?? "(first page)", ex.Message);
                return null;
            }

            foreach (var issue in page.Issues)
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

            // A null/empty token marks the last page.
            if (string.IsNullOrEmpty(page.NextPageToken))
            {
                break;
            }

            nextPageToken = page.NextPageToken;
        }

        return map;
    }

    /// <summary>Removes the URL scheme (e.g. "https://") so the value is safe in a JQL phrase.</summary>
    private static string StripScheme(string url)
    {
        var schemeIndex = url.IndexOf("://", StringComparison.Ordinal);
        return schemeIndex >= 0 ? url[(schemeIndex + 3)..] : url;
    }

    // -------------------------------------------------------------------------
    // Phase 2a: update status
    // -------------------------------------------------------------------------

    // Returns (Ok, Changed): Ok is false on error; Changed is true only when a transition
    // was actually applied (an empty CSV status is a no-op, not a change or a failure).
    private async Task<(bool Ok, bool Changed)> UpdateStatusAsync(
        string jiraKey, List<string> row, CancellationToken ct)
    {
        var targetStatus = GetField(row, ColStatus);
        if (string.IsNullOrWhiteSpace(targetStatus))
        {
            _logger.LogDebug("{JiraKey}: Status is empty in CSV — skipping status update.", jiraKey);
            return (true, false);
        }

        List<JiraTransition> transitions;
        try
        {
            transitions = await _client.GetTransitionsAsync(jiraKey, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            _logger.LogError("{JiraKey}: Failed to get transitions: {Message}", jiraKey, ex.Message);
            return (false, false);
        }

        var transition = transitions
            .FirstOrDefault(t => t.Name.Equals(targetStatus, StringComparison.Ordinal));

        if (transition is null)
        {
            _logger.LogError(
                "{JiraKey}: No transition to status '{Status}' is available. Available: {Available}",
                jiraKey, targetStatus,
                string.Join(", ", transitions.Select(t => $"'{t.Name}'")));
            return (false, false);
        }

        try
        {
            await _client.ApplyTransitionAsync(jiraKey, transition.Id, ct).ConfigureAwait(false);
            _logger.LogInformation("{JiraKey}: status set to '{Status}'.", jiraKey, targetStatus);
            return (true, true);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
        {
            _logger.LogError("{JiraKey}: Failed to apply transition to '{Status}': {Message}",
                jiraKey, targetStatus, ex.Message);
            return (false, false);
        }
    }

    // -------------------------------------------------------------------------
    // Phase 2b: add missing comments
    // -------------------------------------------------------------------------

    // Returns (Ok, Added): Ok is false on error; Added is the number of comments added.
    private async Task<(bool Ok, int Added)> UpdateCommentsAsync(
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
            return (true, 0);
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
            return (false, 0);
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

        if (toAdd.Count == 0)
        {
            _logger.LogDebug(
                "{JiraKey}: no new comments to add ({Count} CSV comment(s) are not newer than the latest Jira comment).",
                jiraKey, csvComments.Count);
            return (true, 0);
        }

        var added = 0;
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
                added++;
                _logger.LogInformation("{JiraKey}: comment added (dated {Date}).", jiraKey, comment.Date);
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                _logger.LogError("{JiraKey}: Failed to add comment dated {Date}: {Message}",
                    jiraKey, comment.Date, ex.Message);
                return (false, added);
            }
        }

        return (true, added);
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
        _logger.LogInformation("Statuses updated: {StatusUpdated}", stats.StatusUpdated);
        _logger.LogInformation("Comments added: {CommentsAdded}", stats.CommentsAdded);
        _logger.LogInformation("Failed: {Failed}", stats.Failed);
        _logger.LogInformation("Not found in Jira: {NotFound}", stats.NotFound);
        _logger.LogInformation("Skipped (already done): {Skipped}", stats.Skipped);
    }

    private sealed class UpdateStats
    {
        public int Processed     { get; set; }
        public int Failed        { get; set; }
        public int StatusUpdated { get; set; }
        public int CommentsAdded { get; set; }
        public int NotFound      { get; set; }
        public int Skipped       { get; set; }
    }
}
