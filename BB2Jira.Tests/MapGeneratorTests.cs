using BB2Jira.Models.Bitbucket;
using BB2Jira.Models.Mapping;
using BB2Jira.Services;
using Microsoft.Extensions.Logging;
using Xunit;

namespace BB2Jira.Tests;

/// <summary>Tests for map.json generation and merge logic in <see cref="MapGenerator"/>.</summary>
public class MapGeneratorTests
{
    [Theory]
    [InlineData("bug", "Bug")]
    [InlineData("task", "Task")]
    [InlineData("enhancement", "Task")]
    [InlineData("proposal", "Task")]
    [InlineData("epic", "Task")]
    public void WhenKindResolvedThenExpectedJiraType(string kind, string expected)
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = kind } },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Equal(expected, map.Kind[kind]);
    }

    [Theory]
    [InlineData("new", "Backlog")]
    [InlineData("open", "In Development")]
    [InlineData("resolved", "Ready for Release")]
    [InlineData("on hold", "Planned")]
    [InlineData("invalid", "Canceled")]
    [InlineData("duplicate", "Canceled")]
    [InlineData("wontfix", "Canceled")]
    [InlineData("frozen", "Backlog")]
    public void WhenStatusResolvedThenExpectedJiraStatus(string status, string expected)
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Status = status } },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Equal(expected, map.Status[status]);
    }

    [Theory]
    [InlineData("trivial", "Lowest")]
    [InlineData("minor", "Low")]
    [InlineData("major", "Medium")]
    [InlineData("critical", "High")]
    [InlineData("blocker", "Highest")]
    [InlineData("urgent", "Medium")]
    public void WhenPriorityResolvedThenExpectedJiraPriority(string priority, string expected)
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Priority = priority } },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Equal(expected, map.Priority[priority]);
    }

    [Fact]
    public void WhenUserHasAccountIdThenAccountIdIsKey()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue
                {
                    Id = 1,
                    Reporter = new BitbucketUser { AccountId = "557058:abc", DisplayName = "Ivan Ivanov" },
                },
            },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.True(map.Users.ContainsKey("557058:abc"));
    }

    [Fact]
    public void WhenUserHasNoAccountIdThenDisplayNameIsKey()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue
                {
                    Id = 1,
                    Assignee = new BitbucketUser { DisplayName = "Petr Petrov" },
                },
            },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.True(map.Users.ContainsKey("Petr Petrov"));
    }

    [Fact]
    public void WhenUserCollectedThenJiraDisplayNameDefaultsToBitbucket()
    {
        var export = new BitbucketExport
        {
            Comments =
            {
                new BitbucketComment { Issue = 1, User = new BitbucketUser { AccountId = "id1", DisplayName = "Anna" } },
            },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Equal("Anna", map.Users["id1"].JiraDisplayName);
    }

    [Fact]
    public void WhenMilestoneOnlyInReferenceThenItIsAdded()
    {
        var export = new BitbucketExport
        {
            Milestones = { new BitbucketMilestone { Name = "MVP-1" } },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Equal("MVP-1", map.Milestone["MVP-1"]);
    }

    [Fact]
    public void WhenVersionOnlyInIssueThenItIsAdded()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Version = new BitbucketVersion { Name = "1.1" } },
            },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Equal("1.1", map.Version["1.1"]);
    }

    [Fact]
    public void WhenExistingManualMappingThenItIsPreserved()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Status = "open" } },
        };
        var existing = new MapFile();
        existing.Status["open"] = "Custom Status";

        var map = MapGenerator.Build(export, existing);

        Assert.Equal("Custom Status", map.Status["open"]);
    }

    [Fact]
    public void WhenExistingValueNotInExportThenItIsNotRemoved()
    {
        var export = new BitbucketExport();
        var existing = new MapFile();
        existing.Kind["legacy"] = "Task";

        var map = MapGenerator.Build(export, existing);

        Assert.Equal("Task", map.Kind["legacy"]);
    }

    [Fact]
    public void WhenExistingUserHasJiraAccountIdThenItIsPreserved()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Reporter = new BitbucketUser { AccountId = "id1", DisplayName = "Anna" } },
            },
        };
        var existing = new MapFile();
        existing.Users["id1"] = new UserMapping
        {
            BitbucketDisplayName = "Anna",
            JiraAccountId = "jira-123",
            JiraDisplayName = "Anna",
        };

        var map = MapGenerator.Build(export, existing);

        Assert.Equal("jira-123", map.Users["id1"].JiraAccountId);
    }

    [Fact]
    public void WhenEmptyKindThenNotAddedToMap()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "  " } },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Empty(map.Kind);
    }

    [Fact]
    public void WhenUserOnlyInLogThenItIsCollected()
    {
        var export = new BitbucketExport
        {
            Logs =
            {
                new BitbucketLog { Issue = 1, User = new BitbucketUser { AccountId = "log-user" } },
            },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.True(map.Users.ContainsKey("log-user"));
    }

    [Fact]
    public void WhenCollectedUserThenJiraAccountIdAndEmailDefaultToEmpty()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Reporter = new BitbucketUser { AccountId = "id1", DisplayName = "Anna" } },
            },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Equal(string.Empty, map.Users["id1"].JiraAccountId);
        Assert.Equal(string.Empty, map.Users["id1"].JiraEmail);
    }

    [Fact]
    public void WhenMilestoneOnlyInIssueThenItIsAdded()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Milestone = new BitbucketMilestone { Name = "MVP-3" } },
            },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Equal("MVP-3", map.Milestone["MVP-3"]);
    }

    [Fact]
    public void WhenVersionOnlyInReferenceThenItIsAdded()
    {
        var export = new BitbucketExport
        {
            Versions = { new BitbucketVersion { Name = "2.0" } },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Equal("2.0", map.Version["2.0"]);
    }

    [Fact]
    public void WhenMultipleKindsThenKeysAreSortedAlphabetically()
    {
        var export = new BitbucketExport
        {
            Issues =
            {
                new BitbucketIssue { Id = 1, Kind = "task" },
                new BitbucketIssue { Id = 2, Kind = "bug" },
                new BitbucketIssue { Id = 3, Kind = "enhancement" },
            },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Equal(new[] { "bug", "enhancement", "task" }, map.Kind.Keys.ToArray());
    }

    [Fact]
    public void WhenSameValueInReferenceAndIssueThenAddedOnce()
    {
        var export = new BitbucketExport
        {
            Milestones = { new BitbucketMilestone { Name = "MVP-1" } },
            Issues =
            {
                new BitbucketIssue { Id = 1, Milestone = new BitbucketMilestone { Name = "MVP-1" } },
            },
        };

        var map = MapGenerator.Build(export, new MapFile());

        Assert.Single(map.Milestone);
    }

    [Fact]
    public void WhenOutputFileExistsThenOverwriteWarningIsLogged()
    {
        var logger = new CapturingLogger();
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug" } },
        };

        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, "{}");
        try
        {
            MapGenerator.Generate(export, path, logger);

            Assert.Contains(logger.Messages, m => m.Contains("will be overwritten", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void WhenOutputFileDoesNotExistThenNoOverwriteWarning()
    {
        var logger = new CapturingLogger();
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug" } },
        };

        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        try
        {
            MapGenerator.Generate(export, path, logger);

            Assert.DoesNotContain(logger.Messages, m => m.Contains("will be overwritten", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // Captures formatted log messages.
    private sealed class CapturingLogger : ILogger
    {
        public List<string> Messages { get; } = new();

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
            Messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
