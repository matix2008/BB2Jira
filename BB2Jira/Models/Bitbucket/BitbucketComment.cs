using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Комментарий к задаче (элемент comments[]).
/// </summary>
public sealed class BitbucketComment
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Идентификатор задачи, к которой относится комментарий.</summary>
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
