using BB2Jira.Services;
using Xunit;

namespace BB2Jira.Tests;

public class BitbucketLoaderTests
{
    [Fact]
    public void WhenContentIsObjectThenRawIsParsed()
    {
        const string json = """
        {
          "issues": [
            { "id": 1, "kind": "bug", "content": { "raw": "Hello", "markup": "markdown", "html": "<p>Hello</p>" } }
          ]
        }
        """;

        var export = BitbucketLoader.Parse(json);

        Assert.Equal("Hello", export.Issues[0].Content!.Text);
    }

    [Fact]
    public void WhenContentIsStringThenItIsParsed()
    {
        const string json = """
        { "issues": [ { "id": 1, "kind": "bug", "content": "Plain text" } ] }
        """;

        var export = BitbucketLoader.Parse(json);

        Assert.Equal("Plain text", export.Issues[0].Content!.Text);
    }

    [Fact]
    public void WhenContentIsNullThenContentIsNull()
    {
        const string json = """
        { "issues": [ { "id": 1, "kind": "bug", "content": null } ] }
        """;

        var export = BitbucketLoader.Parse(json);

        Assert.Null(export.Issues[0].Content);
    }

    [Fact]
    public void WhenMilestoneIsObjectThenNameIsParsed()
    {
        const string json = """
        { "issues": [ { "id": 1, "kind": "bug", "milestone": { "id": 5, "name": "MVP-1" } } ] }
        """;

        var export = BitbucketLoader.Parse(json);

        Assert.Equal("MVP-1", export.Issues[0].Milestone!.Name);
    }

    [Fact]
    public void WhenUserHasAccountIdThenKeyIsAccountId()
    {
        const string json = """
        {
          "issues": [
            { "id": 1, "kind": "bug", "reporter": { "account_id": "557058:abc", "display_name": "Ivan" } }
          ]
        }
        """;

        var export = BitbucketLoader.Parse(json);

        Assert.Equal("557058:abc", export.Issues[0].Reporter!.Key);
    }

    [Fact]
    public void WhenInvalidJsonThenThrowsInvalidData()
    {
        Assert.Throws<InvalidDataException>(() => BitbucketLoader.Parse("{ not json"));
    }
}
