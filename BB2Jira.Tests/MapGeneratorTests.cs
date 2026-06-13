using BB2Jira.Logging;
using BB2Jira.Models.Bitbucket;
using BB2Jira.Models.Mapping;
using BB2Jira.Services;
using Xunit;

namespace BB2Jira.Tests;

public class MapGeneratorTests
{
    private static AppLogger NewLogger() => new(echoToConsole: false);

    [Fact]
    public void WhenKnownKindThenDefaultMappingIsUsed()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "bug" } },
        };

        var map = MapGenerator.Build(export, new MapFile(), NewLogger());

        Assert.Equal("Bug", map.Kind["bug"]);
    }

    [Fact]
    public void WhenUnknownKindThenDefaultsToTask()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "epic" } },
        };

        var map = MapGenerator.Build(export, new MapFile(), NewLogger());

        Assert.Equal("Task", map.Kind["epic"]);
    }

    [Fact]
    public void WhenUnknownStatusThenDefaultsToBacklog()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Status = "frozen" } },
        };

        var map = MapGenerator.Build(export, new MapFile(), NewLogger());

        Assert.Equal("Backlog", map.Status["frozen"]);
    }

    [Fact]
    public void WhenUnknownPriorityThenDefaultsToMedium()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Priority = "urgent" } },
        };

        var map = MapGenerator.Build(export, new MapFile(), NewLogger());

        Assert.Equal("Medium", map.Priority["urgent"]);
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

        var map = MapGenerator.Build(export, new MapFile(), NewLogger());

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

        var map = MapGenerator.Build(export, new MapFile(), NewLogger());

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

        var map = MapGenerator.Build(export, new MapFile(), NewLogger());

        Assert.Equal("Anna", map.Users["id1"].JiraDisplayName);
    }

    [Fact]
    public void WhenMilestoneOnlyInReferenceThenItIsAdded()
    {
        var export = new BitbucketExport
        {
            Milestones = { new BitbucketMilestone { Name = "MVP-1" } },
        };

        var map = MapGenerator.Build(export, new MapFile(), NewLogger());

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

        var map = MapGenerator.Build(export, new MapFile(), NewLogger());

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

        var map = MapGenerator.Build(export, existing, NewLogger());

        Assert.Equal("Custom Status", map.Status["open"]);
    }

    [Fact]
    public void WhenExistingValueNotInExportThenItIsNotRemoved()
    {
        var export = new BitbucketExport();
        var existing = new MapFile();
        existing.Kind["legacy"] = "Task";

        var map = MapGenerator.Build(export, existing, NewLogger());

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

        var map = MapGenerator.Build(export, existing, NewLogger());

        Assert.Equal("jira-123", map.Users["id1"].JiraAccountId);
    }

    [Fact]
    public void WhenEmptyKindThenNotAddedToMap()
    {
        var export = new BitbucketExport
        {
            Issues = { new BitbucketIssue { Id = 1, Kind = "  " } },
        };

        var map = MapGenerator.Build(export, new MapFile(), NewLogger());

        Assert.Empty(map.Kind);
    }
}
