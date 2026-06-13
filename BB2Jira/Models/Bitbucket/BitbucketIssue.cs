using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Задача Bitbucket (элемент issues[]).
/// </summary>
public sealed class BitbucketIssue
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("content")]
    public BitbucketContent? Content { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("priority")]
    public string? Priority { get; set; }

    [JsonPropertyName("reporter")]
    public BitbucketUser? Reporter { get; set; }

    [JsonPropertyName("assignee")]
    public BitbucketUser? Assignee { get; set; }

    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    [JsonPropertyName("updated_on")]
    public DateTimeOffset? UpdatedOn { get; set; }

    [JsonPropertyName("milestone")]
    public BitbucketMilestone? Milestone { get; set; }

    [JsonPropertyName("version")]
    public BitbucketVersion? Version { get; set; }
}
