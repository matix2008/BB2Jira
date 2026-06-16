using BB2Jira.Models.Mapping;
using BB2Jira.Services;
using BB2Jira.Services.Jira;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BB2Jira.Tests;

/// <summary>Unit tests for <see cref="JiraUpdater"/> using a stub <see cref="IJiraClient"/>.</summary>
public class JiraUpdaterTests
{
    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static JiraSettings DefaultSettings() => new()
    {
        BaseUrl          = "https://example.atlassian.net",
        ProjectKey       = "PROJ",
        Email            = "test@example.com",
        ApiToken         = "token",
        BitbucketRepoUrl = "https://bitbucket.org/org/repo",
        UpdateStatus     = true,
        UpdateComments   = true,
    };

    private static string MakeCsv(params string[] dataRows)
    {
        const string header =
            "Issue Type,Summary,Description,Status,Priority,Reporter,Assignee,Created,Updated,Fix Version/s,Bitbucket Milestone,Bitbucket Issue ID";
        var lines = new[] { header }.Concat(dataRows);
        return string.Join("\r\n", lines) + "\r\n";
    }

    private static string MakeRow(
        int bbId,
        string status = "Open",
        string comment = "")
    {
        var commentCol = string.IsNullOrEmpty(comment) ? "" : $",{comment}";
        return $"Bug,Summary {bbId},Desc,{status},Medium,rep@x.com,,2024-01-01 00:00:00,2024-01-01 00:00:00,,,{bbId}{commentCol}";
    }

    private static string WriteTempCsv(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(path, content);
        return path;
    }

    private static string TempFile() =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

    private static ILogger Logger() => NullLogger.Instance;

    // Captures log messages so tests can assert on update output.
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

        public bool HasInformation(string fragment) =>
            Entries.Any(e => e.Level == LogLevel.Information &&
                             e.Message.Contains(fragment, StringComparison.OrdinalIgnoreCase));

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    // -------------------------------------------------------------------------
    // Stub IJiraClient
    // -------------------------------------------------------------------------

    private sealed class StubClient : IJiraClient
    {
        public bool ConnectionOk { get; set; } = true;

        /// <summary>Key: BitbucketId, Value: Jira issue key to return in search.</summary>
        public Dictionary<int, string> IssueMap { get; } = new();

        /// <summary>Additional issues that share the same Bitbucket ID (produce duplicates).</summary>
        public List<(int BbId, string JiraKey)> ExtraIssues { get; } = new();

        /// <summary>When set, the stub returns at most this many issues per page (token-based).</summary>
        public int? PageSizeOverride { get; set; }

        /// <summary>Number of times <see cref="SearchAsync"/> was invoked (one call per page).</summary>
        public int SearchCalls { get; private set; }

        /// <summary>
        /// Builds the Jira description for a Bitbucket issue id. Defaults to the service-block
        /// format; tests can override it to emulate Jira's native importer (URL) format.
        /// </summary>
        public Func<int, string> DescribeIssue { get; set; } =
            id => $"Some text\r\n---\r\nImported from Bitbucket\r\nBitbucket Issue ID: {id}";

        public List<JiraTransition> Transitions { get; set; } =
            [new("10", "Open"), new("20", "In Progress"), new("30", "Done")];

        /// <summary>Current status returned by GetCurrentStatusAsync. Default is empty (never matches target).</summary>
        public string CurrentStatus { get; set; } = string.Empty;

        public List<JiraComment> ExistingComments { get; set; } = [];

        public List<string> AddedComments { get; } = new();
        public List<string> UpdatedComments { get; } = new();
        public List<string> AppliedTransitions { get; } = new();
        public int ResolveDisplayNameCalls { get; private set; }

        public Task<bool> TestConnectionAsync(CancellationToken ct = default)
            => Task.FromResult(ConnectionOk);

        public Task<JiraSearchPage> SearchAsync(string jql, int maxResults, string? nextPageToken, CancellationToken ct = default)
        {
            SearchCalls++;

            // The token encodes the next start offset; null means the first page.
            var start = string.IsNullOrEmpty(nextPageToken) ? 0 : int.Parse(nextPageToken);
            var pageSize = PageSizeOverride ?? maxResults;

            var all = IssueMap
                .Select(kv => new JiraIssueRef(kv.Value, DescribeIssue(kv.Key)))
                .Concat(ExtraIssues.Select(e => new JiraIssueRef(e.JiraKey, DescribeIssue(e.BbId))))
                .ToList();

            var pageItems = all.Skip(start).Take(pageSize).ToList();
            var nextStart = start + pageItems.Count;
            var token = nextStart < all.Count ? nextStart.ToString() : null;

            return Task.FromResult(new JiraSearchPage(pageItems, token));
        }

        public Task<List<JiraTransition>> GetTransitionsAsync(string issueKey, CancellationToken ct = default)
            => Task.FromResult(Transitions);

        public Task<string> GetCurrentStatusAsync(string issueKey, CancellationToken ct = default)
            => Task.FromResult(CurrentStatus);

        public Task ApplyTransitionAsync(string issueKey, string transitionId, CancellationToken ct = default)
        {
            AppliedTransitions.Add($"{issueKey}:{transitionId}");
            return Task.CompletedTask;
        }

        public Task<List<JiraComment>> GetCommentsAsync(string issueKey, CancellationToken ct = default)
            => Task.FromResult(ExistingComments);

        public Task AddCommentAsync(string issueKey, string text, CancellationToken ct = default)
        {
            AddedComments.Add($"{issueKey}:{text}");
            return Task.CompletedTask;
        }

        public Task UpdateCommentAsync(string issueKey, string commentId, string text, CancellationToken ct = default)
        {
            UpdatedComments.Add($"{issueKey}:{commentId}:{text}");
            return Task.CompletedTask;
        }

        public Task<string> ResolveDisplayNameAsync(string accountId, CancellationToken ct = default)
        {
            ResolveDisplayNameCalls++;
            return Task.FromResult($"User({accountId})");
        }
    }

    // -------------------------------------------------------------------------
    // Connection
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenConnectionFailsThenRunReturnsFalse()
    {
        var client = new StubClient { ConnectionOk = false };
        var csvPath = WriteTempCsv(MakeCsv());
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        var result = await updater.RunAsync(csvPath, TempFile());

        Assert.False(result);
    }

    // -------------------------------------------------------------------------
    // Issue not found in Jira
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenIssueNotFoundInJiraThenRunReturnsTrue()
    {
        // IssueMap is empty → no Jira issue found for BB#1.
        var client = new StubClient();
        var csv = WriteTempCsv(MakeCsv(MakeRow(1)));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        var result = await updater.RunAsync(csv, TempFile());

        Assert.True(result);
        Assert.Empty(client.AppliedTransitions);
    }

    // -------------------------------------------------------------------------
    // Status update
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenStatusFoundInTransitionsThenTransitionApplied()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, status: "Open")));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        var result = await updater.RunAsync(csv, TempFile());

        Assert.True(result);
        Assert.Contains("PROJ-1:10", client.AppliedTransitions); // id "10" = "Open"
    }

    [Fact]
    public async Task WhenStatusNotInTransitionsThenRunReturnsFalse()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, status: "NonExistentStatus")));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        var result = await updater.RunAsync(csv, TempFile());

        Assert.False(result);
        Assert.Empty(client.AppliedTransitions);
    }

    [Fact]
    public async Task WhenAlreadyInTargetStatusThenNoTransitionApplied()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        client.CurrentStatus = "Open"; // Already in target status.
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, status: "Open")));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        var result = await updater.RunAsync(csv, TempFile());

        Assert.True(result);
        Assert.Empty(client.AppliedTransitions);
    }

    [Fact]
    public async Task WhenUpdateStatusFalseThenNoTransitionApplied()
    {
        var settings = DefaultSettings();
        settings.UpdateStatus = false;
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, status: "Open")));
        var updater = new JiraUpdater(client, settings, Logger());

        await updater.RunAsync(csv, TempFile());

        Assert.Empty(client.AppliedTransitions);
    }

    // -------------------------------------------------------------------------
    // Comment update
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenNoExistingCommentsThenAllCsvCommentsAdded()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        var comment = "2024-06-01 10:00:00;acc123;Hello world";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        await updater.RunAsync(csv, TempFile());

        Assert.Single(client.AddedComments);
        Assert.Contains("Hello world", client.AddedComments[0]);
    }

    [Fact]
    public async Task WhenExistingCommentNewerThenCsvCommentSkipped()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        // Existing Jira comment is newer than the CSV comment.
        client.ExistingComments =
        [
            new JiraComment("100", new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero), "some text"),
        ];
        var comment = "2024-06-01 10:00:00;acc123;Old comment";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        await updater.RunAsync(csv, TempFile());

        Assert.Empty(client.AddedComments);
    }

    [Fact]
    public async Task WhenCommentDateNewerThanExistingThenCommentAdded()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        client.ExistingComments =
        [
            new JiraComment("100", new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), "old"),
        ];
        var comment = "2024-06-01 10:00:00;acc123;New comment";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        await updater.RunAsync(csv, TempFile());

        Assert.Single(client.AddedComments);
        Assert.Contains("New comment", client.AddedComments[0]);
    }

    [Fact]
    public async Task WhenCommentHasAccountIdThenDisplayNameResolved()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        var comment = "2024-06-01 10:00:00;acc456;Text";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        await updater.RunAsync(csv, TempFile());

        Assert.True(client.ResolveDisplayNameCalls > 0);
        Assert.Contains("User(acc456)", client.AddedComments[0]);
    }

    [Fact]
    public async Task WhenUpdateCommentsFalseThenNoCommentAdded()
    {
        var settings = DefaultSettings();
        settings.UpdateComments = false;
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        var comment = "2024-06-01 10:00:00;acc123;Hello";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));
        var updater = new JiraUpdater(client, settings, Logger());

        await updater.RunAsync(csv, TempFile());

        Assert.Empty(client.AddedComments);
    }

    // -------------------------------------------------------------------------
    // Resume
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenProgressFileExistsThenAlreadyProcessedRowsSkipped()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        client.IssueMap[2] = "PROJ-2";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, status: "Open"), MakeRow(2, status: "Open")));

        var progressPath = TempFile();
        // Simulate that BB#1 was already processed.
        File.WriteAllText(progressPath, "last_processed=1\n");

        var updater = new JiraUpdater(client, DefaultSettings(), Logger());
        await updater.RunAsync(csv, progressPath);

        // Only PROJ-2 transition should have been applied.
        Assert.DoesNotContain(client.AppliedTransitions, t => t.StartsWith("PROJ-1:", StringComparison.Ordinal));
        Assert.Contains(client.AppliedTransitions, t => t.StartsWith("PROJ-2:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenRunSucceedsThenProgressFileDeleted()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, status: "Open")));
        var progressPath = TempFile();
        File.WriteAllText(progressPath, "last_processed=0\n");

        var updater = new JiraUpdater(client, DefaultSettings(), Logger());
        var result = await updater.RunAsync(csv, progressPath);

        Assert.True(result);
        Assert.False(File.Exists(progressPath));
    }

    // -------------------------------------------------------------------------
    // Search pagination (token-based)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenSearchSpansMultiplePagesThenAllIssuesMapped()
    {
        var client = new StubClient { PageSizeOverride = 2 };
        for (var i = 1; i <= 5; i++)
        {
            client.IssueMap[i] = $"PROJ-{i}";
        }

        var rows = Enumerable.Range(1, 5)
            .Select(i => MakeRow(i, status: "Open"))
            .ToArray();
        var csv = WriteTempCsv(MakeCsv(rows));

        var updater = new JiraUpdater(client, DefaultSettings(), Logger());
        var result = await updater.RunAsync(csv, TempFile());

        Assert.True(result);
        // 5 issues across pages of 2 => 3 search calls (2 + 2 + 1).
        Assert.Equal(3, client.SearchCalls);
        // Every issue must have been transitioned, proving all pages were mapped.
        for (var i = 1; i <= 5; i++)
        {
            Assert.Contains($"PROJ-{i}:10", client.AppliedTransitions);
        }
    }

    // -------------------------------------------------------------------------
    // Match strategy (serviceBlock vs url)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenMatchByUrlThenIssuesMatchedFromBitbucketUrl()
    {
        var settings = DefaultSettings();
        settings.MatchBy = JiraSettings.MatchByUrl;
        settings.BitbucketRepoUrl = "https://bitbucket.org/org/repo";

        var client = new StubClient
        {
            // Emulate Jira's native Bitbucket importer description (URL, no service block).
            DescribeIssue = id =>
                $"<p>Imported from {settings.BitbucketRepoUrl}/issues/{id}/some-slug</p>",
        };
        client.IssueMap[1637] = "CT-1";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1637, status: "Open")));

        var updater = new JiraUpdater(client, settings, Logger());
        var result = await updater.RunAsync(csv, TempFile());

        Assert.True(result);
        Assert.Contains("CT-1:10", client.AppliedTransitions);
    }

    [Fact]
    public async Task WhenMatchByServiceBlockThenUrlOnlyDescriptionNotMatched()
    {
        // Default strategy is serviceBlock; a URL-only description must not match.
        var settings = DefaultSettings();
        settings.BitbucketRepoUrl = "https://bitbucket.org/org/repo";

        var client = new StubClient
        {
            DescribeIssue = id =>
                $"<p>Imported from {settings.BitbucketRepoUrl}/issues/{id}/some-slug</p>",
        };
        client.IssueMap[1637] = "CT-1";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1637, status: "Open")));

        var updater = new JiraUpdater(client, settings, Logger());
        var result = await updater.RunAsync(csv, TempFile());

        Assert.True(result);
        Assert.Empty(client.AppliedTransitions);
    }

    // -------------------------------------------------------------------------
    // Logging of comment additions (problem #1)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenCommentAddedThenInformationIsLogged()
    {
        var logger = new CapturingLogger();
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        var comment = "2024-06-01 10:00:00;acc123;Hello world";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));

        var updater = new JiraUpdater(client, DefaultSettings(), logger);
        await updater.RunAsync(csv, TempFile());

        Assert.True(logger.HasInformation("comment added"));
    }

    [Fact]
    public async Task WhenCommentsAddedThenSummaryReportsCount()
    {
        var logger = new CapturingLogger();
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        var comment = "2024-06-01 10:00:00;acc123;Hello world";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));

        var updater = new JiraUpdater(client, DefaultSettings(), logger);
        await updater.RunAsync(csv, TempFile());

        Assert.True(logger.HasInformation("Comments added: 1"));
    }

    // -------------------------------------------------------------------------
    // Resume after failure (problem #2)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenIssueFailsThenProgressDoesNotAdvancePastIt()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        client.IssueMap[2] = "PROJ-2";
        client.IssueMap[3] = "PROJ-3";

        // BB#2 has a status with no matching transition => it fails.
        var csv = WriteTempCsv(MakeCsv(
            MakeRow(1, status: "Open"),
            MakeRow(2, status: "NonExistentStatus"),
            MakeRow(3, status: "Open")));

        var progressPath = TempFile();
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());
        var result = await updater.RunAsync(csv, progressPath);

        Assert.False(result);
        // Watermark must stay at the last consecutively-successful issue (BB#1),
        // so the failed BB#2 is retried on the next run.
        Assert.True(File.Exists(progressPath));
        Assert.Contains("last_processed=1", File.ReadAllText(progressPath));
    }

    [Fact]
    public async Task WhenFailedIssueRetriedOnNextRunThenItIsProcessed()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        client.IssueMap[2] = "PROJ-2";

        // First run: BB#2 fails (unknown status) so progress stays at BB#1.
        var failingCsv = WriteTempCsv(MakeCsv(
            MakeRow(1, status: "Open"),
            MakeRow(2, status: "NonExistentStatus")));
        var progressPath = TempFile();
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());
        await updater.RunAsync(failingCsv, progressPath);

        // Second run: BB#2 now has a valid status and must be retried (not skipped).
        var fixedCsv = WriteTempCsv(MakeCsv(
            MakeRow(1, status: "Open"),
            MakeRow(2, status: "Open")));
        var result = await updater.RunAsync(fixedCsv, progressPath);

        Assert.True(result);
        Assert.Contains("PROJ-2:10", client.AppliedTransitions);
    }

    // -------------------------------------------------------------------------
    // Comment mode: all (synchronize all comments)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenCommentModeAllAndCommentMatchesThenNoUpdate()
    {
        var settings = DefaultSettings();
        settings.CommentMode = JiraSettings.CommentModeAll;

        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        // Existing Jira comment matches the expected formatted text.
        client.ExistingComments =
        [
            new JiraComment("200", DateTimeOffset.MinValue, "2024-06-01 10:00:00 | User(acc123) | Hello world"),
        ];
        var comment = "2024-06-01 10:00:00;acc123;Hello world";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));
        var updater = new JiraUpdater(client, settings, Logger());

        await updater.RunAsync(csv, TempFile());

        Assert.Empty(client.AddedComments);
        Assert.Empty(client.UpdatedComments);
    }

    [Fact]
    public async Task WhenCommentModeAllAndCommentDiffersThenCommentUpdated()
    {
        var settings = DefaultSettings();
        settings.CommentMode = JiraSettings.CommentModeAll;

        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        // Existing text differs from the CSV.
        client.ExistingComments =
        [
            new JiraComment("200", DateTimeOffset.MinValue, "2024-06-01 10:00:00 | User(acc123) | Old text"),
        ];
        var comment = "2024-06-01 10:00:00;acc123;New text";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));
        var updater = new JiraUpdater(client, settings, Logger());

        await updater.RunAsync(csv, TempFile());

        Assert.Empty(client.AddedComments);
        Assert.Single(client.UpdatedComments);
        Assert.Contains("PROJ-1:200:", client.UpdatedComments[0]);
        Assert.Contains("New text", client.UpdatedComments[0]);
    }

    [Fact]
    public async Task WhenCommentModeAllAndMoreCsvCommentsThanExistingThenNewOnesAdded()
    {
        var settings = DefaultSettings();
        settings.CommentMode = JiraSettings.CommentModeAll;

        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        // One existing comment that matches; CSV has two comments total.
        client.ExistingComments =
        [
            new JiraComment("200", DateTimeOffset.MinValue, "2024-06-01 10:00:00 | User(acc1) | First"),
        ];
        // Two CSV comments: first matches, second is new.
        var csv = WriteTempCsv(MakeCsv(
            $"Bug,Summary 1,Desc,Open,Medium,rep@x.com,,2024-01-01 00:00:00,2024-01-01 00:00:00,,,1,2024-06-01 10:00:00;acc1;First,2024-06-02 12:00:00;acc2;Second"));
        var updater = new JiraUpdater(client, settings, Logger());

        await updater.RunAsync(csv, TempFile());

        Assert.Empty(client.UpdatedComments);
        Assert.Single(client.AddedComments);
        Assert.Contains("Second", client.AddedComments[0]);
    }

    [Fact]
    public async Task WhenCommentModeNewThenDefaultBehaviorPreserved()
    {
        // Explicit "new" mode — same as default: only adds newer comments.
        var settings = DefaultSettings();
        settings.CommentMode = JiraSettings.CommentModeNew;

        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        client.ExistingComments =
        [
            new JiraComment("200", new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero), "existing"),
        ];
        var comment = "2024-06-01 10:00:00;acc123;Older comment";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));
        var updater = new JiraUpdater(client, settings, Logger());

        await updater.RunAsync(csv, TempFile());

        Assert.Empty(client.AddedComments);
        Assert.Empty(client.UpdatedComments);
    }

    [Fact]
    public async Task WhenCommentModeAllThenSummaryReportsUpdatedCount()
    {
        var settings = DefaultSettings();
        settings.CommentMode = JiraSettings.CommentModeAll;

        var logger = new CapturingLogger();
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        client.ExistingComments =
        [
            new JiraComment("200", DateTimeOffset.MinValue, "old text"),
        ];
        var comment = "2024-06-01 10:00:00;acc123;New text";
        var csv = WriteTempCsv(MakeCsv(MakeRow(1, comment: comment)));
        var updater = new JiraUpdater(client, settings, logger);

        await updater.RunAsync(csv, TempFile());

        Assert.True(logger.HasInformation("Comments updated: 1"));
    }

    // -------------------------------------------------------------------------
    // Duplicate closing
    // -------------------------------------------------------------------------

    [Fact]
    public async Task WhenDuplicateExistsAndDuplicateStatusSetThenDuplicateIsClosed()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        // PROJ-10 is a duplicate (same BB ID 1).
        client.ExtraIssues.Add((1, "PROJ-10"));

        var csv = WriteTempCsv(MakeCsv(MakeRow(1, status: "Open")));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        var result = await updater.RunAsync(csv, TempFile(), duplicateStatus: "Done");

        Assert.True(result);
        // PROJ-10 should be transitioned to "Done" (id "30").
        Assert.Contains("PROJ-10:30", client.AppliedTransitions);
    }

    [Fact]
    public async Task WhenDuplicateExistsAndDuplicateStatusEmptyThenDuplicateNotClosed()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        client.ExtraIssues.Add((1, "PROJ-10"));

        var csv = WriteTempCsv(MakeCsv(MakeRow(1, status: "Open")));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        await updater.RunAsync(csv, TempFile(), duplicateStatus: null);

        // Only PROJ-1 transition should exist (for status "Open").
        Assert.DoesNotContain(client.AppliedTransitions, t => t.StartsWith("PROJ-10:", StringComparison.Ordinal));
    }

    [Fact]
    public async Task WhenDuplicateAlreadyInTargetStatusThenNoTransitionApplied()
    {
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        client.ExtraIssues.Add((1, "PROJ-10"));
        client.CurrentStatus = "Done"; // Already closed.

        var csv = WriteTempCsv(MakeCsv(MakeRow(1, status: "Done")));
        var updater = new JiraUpdater(client, DefaultSettings(), Logger());

        await updater.RunAsync(csv, TempFile(), duplicateStatus: "Done");

        // No transitions at all — both primary and duplicate are already in target status.
        Assert.Empty(client.AppliedTransitions);
    }

    [Fact]
    public async Task WhenDuplicatesClosedThenSummaryReportsCount()
    {
        var logger = new CapturingLogger();
        var client = new StubClient();
        client.IssueMap[1] = "PROJ-1";
        client.ExtraIssues.Add((1, "PROJ-10"));
        client.ExtraIssues.Add((1, "PROJ-20"));

        var csv = WriteTempCsv(MakeCsv(MakeRow(1, status: "Open")));
        var updater = new JiraUpdater(client, DefaultSettings(), logger);

        await updater.RunAsync(csv, TempFile(), duplicateStatus: "Done");

        Assert.True(logger.HasInformation("Duplicates closed: 2"));
    }
}
