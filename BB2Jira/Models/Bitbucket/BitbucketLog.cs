using System.Text.Json.Serialization;

namespace BB2Jira.Models.Bitbucket;

/// <summary>
/// Issue change-history entry (an element of logs[]).
/// </summary>
public sealed class BitbucketLog
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>Identifier of the issue the history entry belongs to.</summary>
    [JsonPropertyName("issue")]
    public int Issue { get; set; }

    [JsonPropertyName("user")]
    public BitbucketUser? User { get; set; }

    [JsonPropertyName("field")]
    public string? Field { get; set; }

    [JsonPropertyName("changed_from")]
    public string? ChangedFrom { get; set; }

    [JsonPropertyName("changed_to")]
    public string? ChangedTo { get; set; }

    [JsonPropertyName("created_on")]
    public DateTimeOffset? CreatedOn { get; set; }
}
