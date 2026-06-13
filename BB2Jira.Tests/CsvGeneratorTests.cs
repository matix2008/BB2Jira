using BB2Jira.Logging;
using BB2Jira.Models.Bitbucket;
using BB2Jira.Models.Mapping;
using BB2Jira.Services;
using Xunit;

namespace BB2Jira.Tests;

public class CsvGeneratorTests
{
    private static AppLogger NewLogger() => new(echoToConsole: false);

    private static MapFile MapWithKind(params (string Key, string Value)[] kinds)
    {
        var map = new MapFile();
        foreach (var (key, value) in kinds)
        {
            map.Kind[key] = value;
        }

        return map;
    }

    [Fact]
    public void WhenTitleEmptyThenSummaryUsesIssueId()
    {
        var issue = new BitbucketIssue { Id = 42, Title = null };

        var summary = CsvGenerator.BuildSummary(issue);

        Assert.Equal("Bitbucket issue 42", summary);
    }

    [Fact]
    public void WhenTitlePresentThenSummaryUsesTitle()
    {
        var issue = new BitbucketIssue { Id = 42, Title = "Fix login" };

        var summary = CsvGenerator.BuildSummary(issue);

        Assert.Equal("Fix login", summary);
    }

    [Fact]
    public void WhenContentEmptyThenDescriptionIsServiceBlockOnly()
    {
        var issue = new BitbucketIssue { Id = 7 };

        var description = CsvGenerator.BuildDescription(issue);

        Assert.Equal("---\r\n\r\nImported from Bitbucket\r\nBitbucket Issue ID: 7", description);
    }

    [Fact]
    public void WhenContentPresentThenDescriptionContainsContent()
    {
        var issue = new BitbucketIssue
        {
            Id = 7,
            Content = new BitbucketContent { Raw = "Some text" },
        };

        var description = CsvGenerator.BuildDescription(issue);

        Assert.StartsWith("Some text", description);
    }

    [Fact]
    public void WhenKindMapsToTaskThenRowIsExported()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "task" } },
        };

        var csv = CsvGenerator.BuildCsv(export, MapWithKind(("task", "Task")), NewLogger()).ToString();

        Assert.Contains("Task", csv);
    }

    [Fact]
    public void WhenKindMapsToEpicThenRowIsSkipped()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "proposal" } },
        };

        var csv = CsvGenerator.BuildCsv(export, MapWithKind(("proposal", "Epic")), NewLogger()).ToString();
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines); // только заголовок
    }

    [Fact]
    public void WhenKindMissingInMapThenRowIsSkipped()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "unknown" } },
        };

        var csv = CsvGenerator.BuildCsv(export, new MapFile(), NewLogger()).ToString();
        var lines = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

        Assert.Single(lines);
    }

    [Fact]
    public void WhenPriorityMissingThenMediumIsUsed()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug", Priority = null } },
        };

        var csv = CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug")), NewLogger()).ToString();

        Assert.Contains("Medium", csv);
    }

    [Fact]
    public void WhenCreatedDateThenFormattedAsExpected()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue
                {
                    Id = 1,
                    Kind = "bug",
                    CreatedOn = new DateTimeOffset(2023, 5, 1, 10, 30, 45, TimeSpan.Zero),
                },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug")), NewLogger()).ToString();

        Assert.Contains("2023-05-01 10:30:45", csv);
    }

    [Fact]
    public void WhenReporterMappedThenJiraAccountIdIsUsed()
    {
        var map = MapWithKind(("bug", "Bug"));
        map.Users["id1"] = new UserMapping { JiraAccountId = "jira-1" };
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue
                {
                    Id = 1,
                    Kind = "bug",
                    Reporter = new BitbucketUser { AccountId = "id1", DisplayName = "Anna" },
                },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("jira-1", csv);
    }

    [Fact]
    public void WhenCommentMappedThenFormattedWithJiraUser()
    {
        var map = MapWithKind(("bug", "Bug"));
        map.Users["id1"] = new UserMapping { JiraAccountId = "jira-1" };
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug" } },
            Comments =
            {
                new BitbucketComment
                {
                    Issue = 1,
                    User = new BitbucketUser { AccountId = "id1", DisplayName = "Anna" },
                    Content = new BitbucketContent { Raw = "Looks good" },
                    CreatedOn = new DateTimeOffset(2023, 5, 1, 12, 0, 0, TimeSpan.Zero),
                },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("2023-05-01 12:00:00;jira-1;Looks good", csv);
    }

    [Fact]
    public void WhenCommentAuthorNotMappedThenOriginalAuthorNotePresent()
    {
        var map = MapWithKind(("bug", "Bug"));
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug" } },
            Comments =
            {
                new BitbucketComment
                {
                    Issue = 1,
                    User = new BitbucketUser { DisplayName = "Stranger" },
                    Content = new BitbucketContent { Raw = "Hello" },
                    CreatedOn = new DateTimeOffset(2023, 5, 1, 12, 0, 0, TimeSpan.Zero),
                },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("[Original Bitbucket author: Stranger]", csv);
    }

    [Fact]
    public void WhenIssuesUnorderedThenRowsSortedById()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 2, Kind = "bug", Title = "Second" },
                new BitbucketIssue { Id = 1, Kind = "bug", Title = "First" },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug")), NewLogger()).ToString();

        Assert.True(csv.IndexOf("First", StringComparison.Ordinal) < csv.IndexOf("Second", StringComparison.Ordinal));
    }
}
