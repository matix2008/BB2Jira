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

    // -------------------------------------------------------------------------
    // Stub IJiraClient
    // -------------------------------------------------------------------------

    private sealed class StubClient : IJiraClient
    {
        public bool ConnectionOk { get; set; } = true;

        /// <summary>Key: BitbucketId, Value: Jira issue key to return in search.</summary>
        public Dictionary<int, string> IssueMap { get; } = new();

        public List<JiraTransition> Transitions { get; set; } =
            [new("10", "Open"), new("20", "In Progress"), new("30", "Done")];

        public List<JiraComment> ExistingComments { get; set; } = [];

        public List<string> AddedComments { get; } = new();
        public List<string> AppliedTransitions { get; } = new();
        public int ResolveDisplayNameCalls { get; private set; }

        public Task<bool> TestConnectionAsync(CancellationToken ct = default)
            => Task.FromResult(ConnectionOk);

        public Task<List<JiraIssueRef>> SearchAsync(string jql, int startAt, int maxResults, CancellationToken ct = default)
        {
            var refs = IssueMap
                .Select(kv => new JiraIssueRef(
                    kv.Value,
                    $"Some text\nImported from https://bitbucket.org/org/repo/issues/{kv.Key}"))
                .Skip(startAt)
                .Take(maxResults)
                .ToList();
            return Task.FromResult(refs);
        }

        public Task<List<JiraTransition>> GetTransitionsAsync(string issueKey, CancellationToken ct = default)
            => Task.FromResult(Transitions);

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
            new JiraComment(new DateTimeOffset(2024, 12, 1, 0, 0, 0, TimeSpan.Zero), "some text"),
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
            new JiraComment(new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero), "old"),
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
}
