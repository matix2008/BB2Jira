using BB2Jira.Models.Bitbucket;
using BB2Jira.Models.Mapping;
using BB2Jira.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BB2Jira.Tests;

/// <summary>Tests for import.csv generation in <see cref="CsvGenerator"/>.</summary>
public class CsvGeneratorTests
{
    private static ILogger NewLogger() => NullLogger.Instance;

    private static MapFile MapWithKind(params (string Key, string Value)[] kinds)
    {
        var map = new MapFile();
        foreach (var (key, value) in kinds)
        {
            map.Kind[key] = value;
        }

        return map;
    }

    // Minimal RFC 4180 parser so quoted fields containing commas or CRLF are handled correctly.
    private static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new System.Text.StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var c = csv[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < csv.Length && csv[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r' when i + 1 < csv.Length && csv[i + 1] == '\n':
                    row.Add(field.ToString());
                    field.Clear();
                    rows.Add(row);
                    row = new List<string>();
                    i++;
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row);
        }

        return rows;
    }

    // Captures formatted log messages so tests can assert on summary output.
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();

            public void Dispose()
            {
            }
        }
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

        Assert.Single(lines); // header only
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

    [Fact]
    public void WhenStatusMappedThenJiraStatusIsUsed()
    {
        var map = MapWithKind(("bug", "Bug"));
        map.Status["open"] = "In Development";
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug", Status = "open" } },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("In Development", csv);
    }

    [Fact]
    public void WhenPriorityMappedThenJiraPriorityIsUsed()
    {
        var map = MapWithKind(("bug", "Bug"));
        map.Priority["critical"] = "High";
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug", Priority = "critical" } },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("High", csv);
    }

    [Fact]
    public void WhenFixVersionMappedThenJiraVersionIsUsed()
    {
        var map = MapWithKind(("bug", "Bug"));
        map.Version["1.0"] = "Release 1.0";
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug", Version = new BitbucketVersion { Name = "1.0" } } },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("Release 1.0", csv);
    }

    [Fact]
    public void WhenMilestoneMappedThenJiraMilestoneIsUsed()
    {
        var map = MapWithKind(("bug", "Bug"));
        map.Milestone["MVP-1"] = "Sprint 1";
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug", Milestone = new BitbucketMilestone { Name = "MVP-1" } } },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("Sprint 1", csv);
    }

    [Fact]
    public void WhenReporterHasNoAccountIdThenEmailIsUsed()
    {
        var map = MapWithKind(("bug", "Bug"));
        map.Users["id1"] = new UserMapping { JiraEmail = "anna@example.com" };
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Kind = "bug", Reporter = new BitbucketUser { AccountId = "id1" } },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("anna@example.com", csv);
    }

    [Fact]
    public void WhenReporterNotMappedThenReporterFieldIsEmpty()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Kind = "bug", Reporter = new BitbucketUser { DisplayName = "Stranger" } },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug")), NewLogger()).ToString();
        var dataRow = ParseCsv(csv)[1];

        // Columns: [5] Reporter.
        Assert.Equal(string.Empty, dataRow[5]);
    }

    [Fact]
    public void WhenAssigneeMappedThenJiraAccountIdIsUsed()
    {
        var map = MapWithKind(("bug", "Bug"));
        map.Users["id1"] = new UserMapping { JiraAccountId = "jira-assignee" };
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Kind = "bug", Assignee = new BitbucketUser { AccountId = "id1" } },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("jira-assignee", csv);
    }

    [Fact]
    public void WhenAssigneeAbsentThenAssigneeFieldIsEmpty()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug", Assignee = null } },
        };

        var csv = CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug")), NewLogger()).ToString();
        var dataRow = ParseCsv(csv)[1];

        // Columns: [6] Assignee.
        Assert.Equal(string.Empty, dataRow[6]);
    }

    [Fact]
    public void WhenUpdatedPresentThenUpdatedValueIsUsed()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue
                {
                    Id = 1,
                    Kind = "bug",
                    CreatedOn = new DateTimeOffset(2023, 5, 1, 0, 0, 0, TimeSpan.Zero),
                    UpdatedOn = new DateTimeOffset(2023, 6, 2, 8, 15, 0, TimeSpan.Zero),
                },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug")), NewLogger()).ToString();

        Assert.Contains("2023-06-02 08:15:00", csv);
    }

    [Fact]
    public void WhenUpdatedAbsentThenCreatedValueIsUsed()
    {
        var created = new DateTimeOffset(2023, 5, 1, 10, 30, 45, TimeSpan.Zero);
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Kind = "bug", CreatedOn = created, UpdatedOn = null },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug")), NewLogger()).ToString();
        var dataRow = ParseCsv(csv)[1];

        // Columns: [7] Created, [8] Updated.
        Assert.Equal("2023-05-01 10:30:45", dataRow[7]);
        Assert.Equal("2023-05-01 10:30:45", dataRow[8]);
    }

    [Fact]
    public void WhenIssueExportedThenBitbucketIssueIdColumnHasId()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 99, Kind = "bug" } },
        };

        var csv = CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug")), NewLogger()).ToString();
        var dataRow = ParseCsv(csv)[1];

        // Column [11] is Bitbucket Issue ID.
        Assert.Equal("99", dataRow[11]);
    }

    [Fact]
    public void WhenHistoryMappedThenFormattedWithJiraUser()
    {
        var map = MapWithKind(("bug", "Bug"));
        map.Users["id1"] = new UserMapping { JiraAccountId = "jira-1" };
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug" } },
            Logs =
            {
                new BitbucketLog
                {
                    Issue = 1,
                    User = new BitbucketUser { AccountId = "id1" },
                    Field = "status",
                    ChangedFrom = "new",
                    ChangedTo = "open",
                    CreatedOn = new DateTimeOffset(2023, 5, 1, 9, 0, 0, TimeSpan.Zero),
                },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("[Bitbucket history] status: new", csv);
        Assert.Contains("jira-1", csv);
    }

    [Fact]
    public void WhenHistoryAuthorNotMappedThenOriginalAuthorNotePresent()
    {
        var map = MapWithKind(("bug", "Bug"));
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug" } },
            Logs =
            {
                new BitbucketLog
                {
                    Issue = 1,
                    User = new BitbucketUser { DisplayName = "Stranger" },
                    Field = "status",
                    ChangedFrom = "new",
                    ChangedTo = "open",
                    CreatedOn = new DateTimeOffset(2023, 5, 1, 9, 0, 0, TimeSpan.Zero),
                },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        Assert.Contains("Original Bitbucket author: Stranger", csv);
    }

    [Fact]
    public void WhenCommentsAndLogsThenEventsSortedByCreatedOn()
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
                    Content = new BitbucketContent { Raw = "later comment" },
                    CreatedOn = new DateTimeOffset(2023, 5, 3, 0, 0, 0, TimeSpan.Zero),
                },
            },
            Logs =
            {
                new BitbucketLog
                {
                    Issue = 1,
                    Field = "status",
                    ChangedFrom = "new",
                    ChangedTo = "open",
                    CreatedOn = new DateTimeOffset(2023, 5, 1, 0, 0, 0, TimeSpan.Zero),
                },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();

        // The earlier history event must precede the later comment.
        Assert.True(
            csv.IndexOf("[Bitbucket history]", StringComparison.Ordinal) <
            csv.IndexOf("later comment", StringComparison.Ordinal));
    }

    [Fact]
    public void WhenMultipleEventsThenCommentColumnRepeatsInHeader()
    {
        var map = MapWithKind(("bug", "Bug"));
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug" } },
            Comments =
            {
                new BitbucketComment { Issue = 1, Content = new BitbucketContent { Raw = "c1" }, CreatedOn = new DateTimeOffset(2023, 5, 1, 0, 0, 0, TimeSpan.Zero) },
                new BitbucketComment { Issue = 1, Content = new BitbucketContent { Raw = "c2" }, CreatedOn = new DateTimeOffset(2023, 5, 2, 0, 0, 0, TimeSpan.Zero) },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();
        var header = csv.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[0];

        Assert.Equal(2, header.Split(',').Count(c => c == "Comment"));
    }

    [Fact]
    public void WhenCommentContentEmptyThenEventIsSkipped()
    {
        var map = MapWithKind(("bug", "Bug"));
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug" } },
            Comments =
            {
                new BitbucketComment { Issue = 1, Content = new BitbucketContent { Raw = "  " }, CreatedOn = new DateTimeOffset(2023, 5, 1, 0, 0, 0, TimeSpan.Zero) },
            },
        };

        var csv = CsvGenerator.BuildCsv(export, map, NewLogger()).ToString();
        var dataRow = ParseCsv(csv)[1];

        // 12 base columns + 1 padded (empty) Comment column = 13 cells, last is empty.
        Assert.Equal(13, dataRow.Count);
        Assert.Equal(string.Empty, dataRow[12]);
    }

    [Fact]
    public void WhenBuildingThenIssuesReadCountIsLogged()
    {
        var logger = new CapturingLogger();
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Kind = "bug" },
                new BitbucketIssue { Id = 2, Kind = "bug" },
            },
        };

        CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug")), logger);

        Assert.Contains("Issues read from db-2.0.json: 2", logger.Messages);
    }

    [Fact]
    public void WhenAllImportedThenNoNotImportedWarning()
    {
        var logger = new CapturingLogger();
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug" } },
        };

        CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug")), logger);

        Assert.DoesNotContain(logger.Messages, m => m.StartsWith("Not imported:", StringComparison.Ordinal));
    }

    [Fact]
    public void WhenKindMissingThenNotImportedReasonIsLogged()
    {
        var logger = new CapturingLogger();
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 7, Kind = "unknown" } },
        };

        CsvGenerator.BuildCsv(export, new MapFile(), logger);

        Assert.Contains("Issue 7 not imported: kind 'unknown' is missing in map.json.", logger.Messages);
    }

    [Fact]
    public void WhenTypeNotTaskOrBugThenNotImportedReasonIsLogged()
    {
        var logger = new CapturingLogger();
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 9, Kind = "proposal" } },
        };

        CsvGenerator.BuildCsv(export, MapWithKind(("proposal", "Epic")), logger);

        Assert.Contains("Issue 9 not imported: type 'Epic' is not Task/Bug.", logger.Messages);
    }

    [Fact]
    public void WhenIssuesSkippedThenNotImportedCountMatchesDifference()
    {
        var logger = new CapturingLogger();
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Kind = "bug" },
                new BitbucketIssue { Id = 2, Kind = "unknown" },
                new BitbucketIssue { Id = 3, Kind = "proposal" },
            },
        };

        CsvGenerator.BuildCsv(export, MapWithKind(("bug", "Bug"), ("proposal", "Epic")), logger);

        Assert.Contains("Not imported: 2 of 3 issue(s). Reasons below:", logger.Messages);
        Assert.Contains(logger.Messages, m => m.StartsWith("Issue 2 not imported:", StringComparison.Ordinal));
        Assert.Contains(logger.Messages, m => m.StartsWith("Issue 3 not imported:", StringComparison.Ordinal));
    }
}
