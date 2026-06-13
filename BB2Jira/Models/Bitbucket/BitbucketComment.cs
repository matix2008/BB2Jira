using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Comment on an issue (an element of comments[]).
/// </summary>
public sealed class BitbucketComment
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Identifier of the issue the comment belongs to.</summary>
    [JsonPropertyName("issue")]
    public int Issue { get; set; }

    [JsonPropertyName("content")]
    public BitbucketContent? Content { get; set; }

    [JsonPropertyName("user")]
    public BitbucketUser? User { get; set; }

    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }

    [JsonPropertyName("updated_on")]
    public DateTimeOffset? UpdatedOn { get; set; }
}
